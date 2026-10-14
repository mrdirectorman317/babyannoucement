import io,json,math,struct,sys,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'tools'))
from protocol import *
class Fragments:
    def __init__(self,data):self.data=data
    def recv(self,n):v=self.data[:min(n,2)];self.data=self.data[len(v):];return v
class ProtocolTests(unittest.TestCase):
    def test_golden_header(self):self.assertEqual(header(1,2).hex(),'534d41430101000000000002')
    def test_roundtrip(self):self.assertEqual(read_packet(Fragments(json_packet({'type':'ping'}))),(1,b'{"type":"ping"}'))
    def test_truncated(self):
        with self.assertRaises(EOFError):read_packet(Fragments(header(1,4)+b'a'))
    def test_bad_magic(self):
        with self.assertRaises(ProtocolError):parse_header(b'FAIL'+header(1,0)[4:])
    def test_bad_version(self):
        with self.assertRaises(ProtocolError):parse_header(b'SMAC'+bytes([2])+header(1,0)[5:])
    def test_reserved(self):
        h=bytearray(header(1,0));h[7]=1
        with self.assertRaises(ProtocolError):parse_header(h)
    def test_limit(self):
        with self.assertRaises(ProtocolError):header(1,65537)
    def test_invalid_kind(self):
        with self.assertRaises(ProtocolError):header(9,0)
    def test_jpeg_roundtrip(self):
        f=Frame(2,3,640,360,4,99,b'\xff\xd8\xff\xd9');self.assertEqual(Frame.parse(f.payload()),f)
    def test_jpeg_dimensions(self):
        with self.assertRaises(ProtocolError):Frame(2,3,0,360,4,99,b'\xff\xd8').payload()
    def test_jpeg_generation(self):
        with self.assertRaises(ProtocolError):Frame(2,3,640,360,0,99,b'\xff\xd8').payload()
    def test_jpeg_signature(self):
        with self.assertRaises(ProtocolError):Frame(2,3,640,360,4,99,b'no').payload()
    def test_pcm(self):self.assertEqual(check_pcm(pcm_payload(b'\x00\x00\xff\x7f')),(16000,b'\x00\x00\xff\x7f'))
    def test_pcm_limit(self):
        with self.assertRaises(ProtocolError):pcm_payload(b'\x00'*384002)
    def test_pcm_count(self):
        with self.assertRaises(ProtocolError):check_pcm(struct.pack('>II',16000,10))
    def test_nan_json(self):
        with self.assertRaises(ValueError):json_packet(dict(u=math.nan))
    def test_fixed_corner_resize_math(self):
        # Same least-squares diagonal scale as C# (scalar reference, not Unity execution).
        w,h=1.6,.9;dx,dy=3.2,1.8;s=(dx*w+dy*h)/(w*w+h*h)
        self.assertAlmostEqual(s,2);fixed=(-w/2,-h/2);center=(fixed[0]+w*s/2,fixed[1]+h*s/2)
        self.assertAlmostEqual(center[0]-w*s/2,fixed[0]);self.assertAlmostEqual(center[1]-h*s/2,fixed[1])
if __name__=='__main__':unittest.main()


class VideoFrameTests(unittest.TestCase):
    def test_golden_prefix_matches_swift(self):
        from protocol import VideoFrame
        f=VideoFrame(1,2,640,360,3,4,1,2,b'\x00\x00\x01\x26')
        self.assertEqual(list(f.payload()[:28]),[0,0,0,1,0,0,0,2,2,128,1,104,0,0,0,3,0,0,0,0,0,0,0,4,1,2,0,0])
    def test_roundtrip_and_rejects(self):
        from protocol import VideoFrame,ProtocolError
        f=VideoFrame(7,9,1920,1080,2,99,3,1,b'\x00\x00\x00\x01\x65\x88')
        self.assertEqual(VideoFrame.parse(f.payload()),f)
        for bad in (VideoFrame(0,1,2,2,1,0,0,1,b'\x00\x00\x01\x41'),VideoFrame(1,1,2,2,1,0,0,9,b'\x00\x00\x01\x41'),
                    VideoFrame(1,1,2,2,1,0,0x80,1,b'\x00\x00\x01\x41'),VideoFrame(1,1,2,2,1,0,0,1,b'\x01\x02\x03\x04')):
            with self.assertRaises(ProtocolError):bad.payload()
        with self.assertRaises(ProtocolError):VideoFrame.parse(b'\x00'*28)
    def test_split_access_units(self):
        from protocol import split_access_units,is_key_unit
        sps,pps,idr,p1,p2=b'\x00\x00\x00\x01\x67\x01',b'\x00\x00\x00\x01\x68\x02',b'\x00\x00\x00\x01\x65\x03',b'\x00\x00\x01\x41\x04',b'\x00\x00\x01\x41\x05'
        units=split_access_units(sps+pps+idr+p1+p2)
        self.assertEqual(units,[sps+pps+idr,p1,p2])
        self.assertTrue(is_key_unit(units[0]));self.assertFalse(is_key_unit(units[1]))
