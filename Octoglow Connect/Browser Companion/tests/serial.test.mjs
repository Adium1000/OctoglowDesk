import test from 'node:test';
import assert from 'node:assert/strict';
import {MacroSerial} from '../serial.mjs';
import {SLOTS} from '../protocol.mjs';

function dump() {
  const lines=['CFG 7 1 1 0 1 100 0 1 0 1'];
  for(let tab=1;tab<=5;tab++)lines.push(`COLOR ${tab} 36 171 255`,`FX ${tab} 0`);
  for(let pack=1;pack<=5;pack++)for(let event=0;event<18;event++)lines.push(`TONE ${pack} ${event} 440`);
  for(let tab=1;tab<=5;tab++)for(const slot of SLOTS)lines.push(`MAP ${tab} ${slot} 0 0 0 0 0`);
  return [...lines,'END'];
}

class FakePort {
  constructor(identity='OCTOGLOW MACRO 0.2 READY') {this.identity=identity;this.commands=[];this.opened=false;}
  async open(options) {
    assert.equal(options.baudRate,115200);this.opened=true;
    this.readable=new ReadableStream({start:controller=>{this.controller=controller;}});
    this.writable=new WritableStream({write:chunk=>{
      const command=new TextDecoder().decode(chunk).trim();this.commands.push(command);
      const lines=command==='HELLO'?[this.identity]:command==='DUMP'?dump():['OK'];
      for(const line of lines)this.controller.enqueue(new TextEncoder().encode(`${line}\r\n`));
    }});
  }
  async setSignals(signals){assert.equal(signals.dataTerminalReady,true);}
  async close(){this.opened=false;}
  getInfo(){return {usbVendorId:0x2e8a,usbProductId:1};}
}

test('connects, verifies firmware, reads config and sends a setting',async()=>{
  const port=new FakePort();const serial=new MacroSerial();
  const config=await serial.connect(port);
  assert.equal(config.tabCount,1);assert.equal(serial.connected,true);
  assert.deepEqual(port.commands,['HELLO','DUMP']);
  await serial.command('BUZZERVOL 20');
  assert.equal(port.commands.at(-1),'BUZZERVOL 20');
  await serial.disconnect();
  assert.equal(port.opened,false);assert.equal(serial.connected,false);
});

test('rejects unrelated serial devices before sending settings',async()=>{
  const port=new FakePort('OTHER DEVICE READY');const serial=new MacroSerial();
  await assert.rejects(serial.connect(port),/not an Octoglow Macro/i);
  assert.deepEqual(port.commands,['HELLO']);assert.equal(port.opened,false);
});
