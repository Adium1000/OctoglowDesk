import {HELLO_REPLY, parseDump} from './protocol.mjs';

const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

// One command at a time: unsolicited EVENT/TAB_ACTIVE lines never consume a reply.
export class MacroSerial extends EventTarget {
  constructor() {
    super();
    this.port = null;
    this.reader = null;
    this.writer = null;
    this.pending = null;
    this.commandTail = Promise.resolve();
    this.readTask = null;
    this.disconnectTask = null;
    this.closing = false;
    this.connected = false;
  }

  async connect(port) {
    if (this.port || this.disconnectTask) throw new Error('A serial connection is already active.');
    this.port = port;
    this.closing = false;
    try {
      await port.open({baudRate: 115200});
      // TinyUSB CDC will not send data until DTR is asserted on some RP2040 builds.
      if (port.setSignals) await port.setSignals({dataTerminalReady: true});
      this.reader = port.readable.getReader();
      this.writer = port.writable.getWriter();
      this.readTask = this.#readLoop();
      await delay(750);
      const hello = await this.command('HELLO');
      if (hello.length !== 1 || hello[0] !== HELLO_REPLY)
        throw new Error('This port is not an Octoglow Macro with firmware 0.2.');
      const config = parseDump(await this.command('DUMP'));
      this.connected = true;
      this.dispatchEvent(new CustomEvent('connected', {detail: config}));
      return config;
    } catch (error) {
      await this.disconnect();
      throw error;
    }
  }

  command(text) {
    if (!/^[A-Z0-9 ]+$/.test(text) || text.length > 80) return Promise.reject(new Error('Invalid serial command.'));
    const result = this.commandTail.catch(() => {}).then(() => this.#send(text));
    this.commandTail = result;
    return result;
  }

  async #send(text) {
    if (!this.writer || this.closing) throw new Error('Macro is disconnected.');
    const timeout = text === 'DUMP' ? 20000 : 4000;
    const lines = [];
    let timer;
    let written = false;
    const reply = new Promise((resolve, reject) => {
      this.pending = {text, lines, resolve, reject};
      timer = setTimeout(() => {
        reject(new Error('Macro did not answer in time. Check the USB connection.'));
        void this.disconnect();
      }, timeout);
    });
    try {
      await this.writer.write(new TextEncoder().encode(`${text}\n`));
      written = true;
      return await reply;
    } catch (error) {
      if (!written) {
        // A failed write has no matching reply; reset the USB session.
        this.pending?.reject(error);
        void reply.catch(() => {});
        void this.disconnect();
      }
      throw error;
    } finally {
      clearTimeout(timer);
      if (this.pending?.text === text) this.pending = null;
    }
  }

  #line(line) {
    if (line.startsWith('EVENT ') || line.startsWith('TAB_ACTIVE ')) {
      this.dispatchEvent(new CustomEvent('device-event', {detail: line}));
      return;
    }
    const pending = this.pending;
    if (!pending) return;
    if (line.startsWith('ERR')) { pending.reject(new Error(line)); return; }
    pending.lines.push(line);
    if ((pending.text === 'HELLO' && line.endsWith('READY')) ||
        (pending.text === 'DUMP' && line === 'END') ||
        (pending.text !== 'HELLO' && pending.text !== 'DUMP' && (line === 'OK' || line.startsWith('OK '))))
      pending.resolve(pending.lines);
  }

  async #readLoop() {
    const decoder = new TextDecoder();
    let buffer = '';
    try {
      while (!this.closing) {
        const {value, done} = await this.reader.read();
        if (done) break;
        buffer += decoder.decode(value, {stream: true});
        if (buffer.length > 100000) throw new Error('Unexpected serial response size.');
        let newline;
        while ((newline = buffer.indexOf('\n')) >= 0) {
          const line = buffer.slice(0, newline).replace(/\r$/, '');
          buffer = buffer.slice(newline + 1);
          if (line.length <= 512) this.#line(line);
        }
      }
      if (!this.closing) throw new Error('Macro disconnected from USB.');
    } catch (error) {
      if (!this.closing) {
        this.dispatchEvent(new CustomEvent('error', {detail: error}));
        void this.disconnect();
      }
    } finally {
      try { this.reader?.releaseLock(); } catch { /* Port is already gone. */ }
      this.reader = null;
    }
  }

  async disconnect() {
    if (this.disconnectTask) return this.disconnectTask;
    if (!this.port) return;
    this.closing = true;
    this.connected = false;
    this.pending?.reject(new Error('Macro disconnected.'));
    this.pending = null;
    this.disconnectTask = (async () => {
      try { await this.reader?.cancel(); } catch { /* Unplugged. */ }
      try { await this.readTask; } catch { /* Read loop already ended. */ }
      try { this.writer?.releaseLock(); } catch { /* Unplugged. */ }
      try { await this.port?.close(); } catch { /* Unplugged. */ }
      this.writer = null;
      this.port = null;
      this.readTask = null;
      this.commandTail = Promise.resolve();
      this.disconnectTask = null;
      this.dispatchEvent(new Event('disconnected'));
    })();
    return this.disconnectTask;
  }
}
