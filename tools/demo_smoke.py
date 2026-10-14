#!/usr/bin/env python3
"""End-to-end check of the integration messages against the demo mock host (Python <-> Python).

Covers: permissions flags, an incoming message notification, assistant ask -> thinking/confirm_request/reply,
confirmation, message reply, and a ui_command. It does NOT exercise the Swift host or the Unity client.
"""
from pathlib import Path
import json,socket,subprocess,sys,tempfile,time
r=Path(__file__).resolve().parents[1];sys.path.insert(0,str(r/'tools'))
from protocol import connect,json_packet,read_packet

def expect(tls,predicate,what,timeout=10):
    tls.settimeout(timeout);end=time.time()+timeout
    while time.time()<end:
        kind,data=read_packet(tls)
        if kind!=1:continue
        m=json.loads(data)
        if predicate(m):return m
    raise AssertionError('timed out waiting for '+what)

with tempfile.TemporaryDirectory(prefix='spatialmac-demo-') as td:
    d=Path(td)
    with socket.socket() as s:s.bind(('127.0.0.1',0));port=s.getsockname()[1]
    proc=subprocess.Popen([sys.executable,str(r/'tools/mock_host.py'),'--directory',td,'--create-config','--port',str(port),'--demo'],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
    try:
        for _ in range(100):
            time.sleep(.1)
            if proc.poll() is not None:raise RuntimeError(proc.communicate())
            try:
                with socket.create_connection(('127.0.0.1',port),timeout=.1):break
            except OSError:continue
        else:raise RuntimeError('Mock did not start')
        config=json.loads((d/'client-config.json').read_text())
        control=connect(config,'control');video=connect(config,'video')
        perms=expect(control,lambda m:m.get('type')=='permissions','permissions')
        assert perms['assistantEnabled'] and perms['messagesEnabled'] and perms['replyEnabled'] and not perms['controlEnabled']
        note=expect(control,lambda m:m.get('type')=='notification' and m.get('category')=='message','message notification',timeout=8)
        assert note['title']=='Sam' and note['replyable'] is True
        control.sendall(json_packet(dict(type='assistant_ask',requestID='r1',text='please open safari')))
        expect(control,lambda m:m.get('type')=='assistant_state' and m.get('action')=='thinking','thinking')
        confirm=expect(control,lambda m:m.get('type')=='confirm_request','confirm_request')
        expect(control,lambda m:m.get('type')=='assistant_reply' and m.get('requestID')=='r1','assistant_reply')
        control.sendall(json_packet(dict(type='confirm',confirmID=confirm['confirmID'],action='approve')))
        expect(control,lambda m:m.get('type')=='notification' and 'Approved' in m.get('body',''),'approval echo')
        control.sendall(json_packet(dict(type='message_reply',notificationID=note['notificationID'],text='Saving you a seat')))
        expect(control,lambda m:m.get('type')=='notification' and 'Saving you a seat' in m.get('body',''),'reply echo')
        control.sendall(json_packet(dict(type='assistant_ask',requestID='r2',text='show my room')))
        ui=expect(control,lambda m:m.get('type')=='ui_command','ui_command')
        assert ui['command']=='set_environment' and ui['arg']=='passthrough'
        print('PASS: notification, assistant ask/confirm/reply, message reply and ui_command flows (demo mock).')
        print('NOT TESTED: Swift host, Unity client, real iMessage, real OpenAI.')
    finally:
        proc.terminate()
        try:proc.wait(timeout=3)
        except subprocess.TimeoutExpired:proc.kill();proc.wait()
