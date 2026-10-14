from pathlib import Path
import subprocess,tempfile,socket,time,json,sys
r=Path(__file__).resolve().parents[1];sys.path.insert(0,str(r/'tools'))
from protocol import connect,ProtocolError
with tempfile.TemporaryDirectory(prefix='spatialmac-validation-') as td:
 d=Path(td)
 # Reserve a high loopback port momentarily, then let simulator use it.
 with socket.socket() as s:s.bind(('127.0.0.1',0));port=s.getsockname()[1]
 proc=subprocess.Popen([sys.executable,str(r/'tools/mock_host.py'),'--directory',td,'--create-config','--port',str(port)],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
 try:
  for _ in range(100):
   time.sleep(.1)
   if proc.poll() is not None:raise RuntimeError(proc.communicate())
   try:
    with socket.create_connection(('127.0.0.1',port),timeout=.1):break
   except OSError:continue
  else:raise RuntimeError('Mock did not start')
  p=subprocess.run([sys.executable,str(r/'tools/probe_host.py'),'--config',str(d/'client-config.json'),'--frame','display:1','--output',str(d/'result.jpg')],capture_output=True,text=True,timeout=15)
  if p.returncode:raise RuntimeError(p.stderr)
  assert (d/'result.jpg').read_bytes()==(r/'tests/fixtures/calibration.jpg').read_bytes()
  time.sleep(.25)
  config=json.loads((d/'client-config.json').read_text());config['certificateSHA256']='0'*64
  try:connect(config)
  except ProtocolError as e:assert 'Pinned certificate mismatch' in str(e)
  else:raise AssertionError('Incorrect pin was accepted')
  print('PASS: control and video TLS lanes exercised through hello/catalog/subscribe/frame/unsubscribe.')
  print('PASS: exact synthetic JPEG bytes transferred through pinned TLS.')
  print('PASS: incorrect certificate pin rejected before sending pairing token.')
  print('NOT TESTED: native macOS/Unity implementation, audio lane, headset or real Mac screen capture.')
 finally:
  proc.terminate()
  try:proc.wait(timeout=3)
  except subprocess.TimeoutExpired:proc.kill();proc.wait()
