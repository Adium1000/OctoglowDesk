import test from 'node:test';
import assert from 'node:assert/strict';
import {parseDump, serializeAction, hasTabSwitch, SLOTS} from '../protocol.mjs';

function fixture() {
  const lines = ['CFG 7 2 1 0 1 65 0 1 5 1'];
  for(let tab=1;tab<=5;tab++)lines.push(`COLOR ${tab} 36 171 255`,`FX ${tab} 0`);
  for(let pack=1;pack<=5;pack++)for(let event=0;event<18;event++)lines.push(`TONE ${pack} ${event} 440`);
  for(let tab=1;tab<=5;tab++)for(const slot of SLOTS)
    lines.push(`MAP ${tab} ${slot} ${slot==='b7'?4:0} 0 0 0 0`);
  lines.push('END');
  return lines;
}

test('parses complete V7 dump, including all five tabs and 90 tones',()=>{
  const config=parseDump(fixture());
  assert.equal(config.tabCount,2);
  assert.equal(config.volume,65);
  assert.deepEqual(config.colors[0],[36,171,255]);
  assert.equal(config.tones[4][17],440);
  assert.equal(config.maps[0].b7.type,4);
  assert.equal(hasTabSwitch(config,1,'b7'),false);
  assert.equal(hasTabSwitch(config,1),true);
});

test('parses V8 brightness and per-tab identities without losing V7 compatibility',()=>{
  const lines=fixture();
  lines[0]='CFG 8 2 1 0 1 65 0 1 5 1 42';
  lines.splice(11,0,
    'META 1 2 3 4A6F63', 'META 2 5 1 -', 'META 3 0 0 -',
    'META 4 0 0 -', 'META 5 0 0 -');
  const config=parseDump(lines);
  assert.equal(config.brightness,42);
  assert.deepEqual(config.metadata[0],{tonePack:2,icon:3,name:'Joc'});
  assert.equal(config.metadata[1].name,'');
  const missing=lines.filter(line=>line!=='META 5 0 0 -');
  assert.throws(()=>parseDump(missing),/incomplete/i);
});

test('accepts V9 tab-reordering firmware without changing the dump format',()=>{
  const lines=fixture();
  lines[0]='CFG 9 2 1 0 1 65 0 1 5 1 42';
  lines.splice(11,0,
    'META 1 2 3 4A6F63', 'META 2 5 1 -', 'META 3 0 0 -',
    'META 4 0 0 -', 'META 5 0 0 -');
  const config=parseDump(lines);
  assert.equal(config.protocol,9);
  assert.equal(config.metadata[0].name,'Joc');
});

test('rejects incomplete, duplicate and malformed serial dumps',()=>{
  assert.throws(()=>parseDump(fixture().slice(0,-1)),/incomplete/i);
  const duplicate=fixture();duplicate.splice(1,0,'COLOR 1 3 4 5');
  assert.throws(()=>parseDump(duplicate),/duplicate/i);
  const invalid=fixture();invalid[0]='CFG 7 2 3 0 1 65 0 1 5 1';
  assert.throws(()=>parseDump(invalid),/active tab/i);
  const wrongProtocol=fixture();wrongProtocol[0]='CFG 6 2 1 0 1 65 0 1 5 1';
  assert.throws(()=>parseDump(wrongProtocol),/protocol 7/i);
});

test('V10 preserves named packs and rejects missing, duplicate or invalid names',()=>{
  const lines=fixture();
  lines[0]='CFG 10 2 1 0 1 65 0 1 5 1 42';
  lines.splice(11,0,...Array.from({length:5},(_,i)=>`META ${i+1} 0 0 -`),
    'PACK 1 4269726F75','PACK 2 -','PACK 3 -','PACK 4 -','PACK 5 -');
  assert.deepEqual(parseDump(lines).toneNames,['Birou','','','','']);
  assert.throws(()=>parseDump(lines.filter(l=>l!=='PACK 5 -')),/incomplete/i);
  const duplicate=[...lines];duplicate.splice(1,0,'PACK 1 58');
  assert.throws(()=>parseDump(duplicate),/duplicate/i);
  for(const bad of ['00','0A','7F','GG','C0AF','41'.repeat(25)]) {
    const invalid=lines.map(l=>l.startsWith('PACK 1 ')?`PACK 1 ${bad}`:l);
    assert.throws(()=>parseDump(invalid));
  }
});

test('V11 keeps independent lighting and requires complete validated RGB fields',()=>{
  const lines=fixture();
  lines[0]='CFG 11 2 1 0 1 65 0 1 5 1 42';
  lines.splice(1,0,'RGBBRIGHT 17',...Array.from({length:5},(_,i)=>`RGBFX ${i+1} ${i+1}`),
    ...Array.from({length:5},(_,i)=>`META ${i+1} 0 0 -`),
    ...Array.from({length:5},(_,i)=>`PACK ${i+1} -`));
  const config=parseDump(lines);
  assert.equal(config.brightness,42);
  assert.equal(config.rgbBrightness,17);
  assert.deepEqual(config.effects,[0,0,0,0,0]);
  assert.deepEqual(config.rgbEffects,[1,2,3,4,5]);
  for(const missing of ['RGBBRIGHT 17','RGBFX 5 5'])
    assert.throws(()=>parseDump(lines.filter(l=>l!==missing)),/incomplete/i);
  for(const extra of ['RGBBRIGHT 17','RGBBRIGHT 101','RGBFX 1 2','RGBFX 6 2']) {
    const invalid=[...lines]; invalid.splice(1,0,extra);
    assert.throws(()=>parseDump(invalid));
  }
  const legacy=parseDump(fixture());
  assert.equal(legacy.rgbBrightness,legacy.brightness);
  assert.deepEqual(legacy.rgbEffects,legacy.effects);
});

test('serializes actions without accepting command injection',()=>{
  assert.equal(serializeAction(2,'b3',{type:1,modifiers:3,key1:4,key2:0,value:0}),
    'SETMAP 2 b3 1 3 4 0 0');
  assert.throws(()=>serializeAction(2,'b3\nRESET',{type:0,modifiers:0,key1:0,key2:0,value:0}));
  assert.throws(()=>serializeAction(9,'b3',{type:0,modifiers:0,key1:0,key2:0,value:0}));
});

test('V12 transactional firmware remains readable by the companion',()=>{
  const lines=fixture();
  lines[0]='CFG 12 2 1 0 1 65 0 1 5 1 42';
  lines.splice(1,0,'RGBBRIGHT 17',...Array.from({length:5},(_,i)=>`RGBFX ${i+1} ${i+1}`),
    ...Array.from({length:5},(_,i)=>`META ${i+1} 0 0 -`),
    ...Array.from({length:5},(_,i)=>`PACK ${i+1} -`));
  assert.equal(parseDump(lines).protocol,12);
  assert.equal(parseDump(lines).rgbBrightness,17);
});
