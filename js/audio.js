// Synthesized sound effects, and the microphone loudness used for the yell power-up.
import { clamp } from "./util.js";

class Sfx {
  init() {
    const ac = this.ac = new (window.AudioContext || window.webkitAudioContext)();
    this.master = ac.createGain();
    this.master.gain.value = 0.6;
    this.master.connect(ac.destination);

    // white noise buffer reused by blasts
    this.noise = ac.createBuffer(1, ac.sampleRate, ac.sampleRate);
    const d = this.noise.getChannelData(0);
    for (let i = 0; i < d.length; i++) d[i] = Math.random() * 2 - 1;

    // beam hum: two detuned saws through a wobbling resonant lowpass
    this.beamGain = ac.createGain();
    this.beamGain.gain.value = 0;
    const lf = ac.createBiquadFilter();
    lf.type = "lowpass"; lf.frequency.value = 1800; lf.Q.value = 6;
    for (const f of [140, 143.5]) {
      const o = ac.createOscillator();
      o.type = "sawtooth"; o.frequency.value = f; o.connect(lf); o.start();
    }
    const wobble = ac.createOscillator(), wobbleAmt = ac.createGain();
    wobble.frequency.value = 7; wobbleAmt.gain.value = 400;
    wobble.connect(wobbleAmt).connect(lf.frequency); wobble.start();
    lf.connect(this.beamGain).connect(this.master);

    // charge whine: pitch follows charge level
    this.chargeOsc = ac.createOscillator();
    this.chargeOsc.type = "triangle"; this.chargeOsc.frequency.value = 200;
    this.chargeGain = ac.createGain(); this.chargeGain.gain.value = 0;
    this.chargeOsc.connect(this.chargeGain).connect(this.master);
    this.chargeOsc.start();

    // tornado wind: looping noise through a bandpass
    const wind = ac.createBufferSource();
    wind.buffer = this.noise; wind.loop = true;
    this.windFilter = ac.createBiquadFilter();
    this.windFilter.type = "bandpass"; this.windFilter.frequency.value = 400; this.windFilter.Q.value = 0.7;
    this.windGain = ac.createGain(); this.windGain.gain.value = 0;
    wind.connect(this.windFilter).connect(this.windGain).connect(this.master);
    wind.start();
  }
  setWind(level) {
    if (!this.ready) return;
    const t = this.ac.currentTime;
    this.windGain.gain.setTargetAtTime(level * 0.35, t, 0.1);
    this.windFilter.frequency.setTargetAtTime(300 + 900 * level + 200 * Math.sin(t * 6), t, 0.1);
  }
  fireball() {
    if (!this.ready) return;
    const ac = this.ac, t = ac.currentTime;
    const src = ac.createBufferSource(); src.buffer = this.noise;
    const f = ac.createBiquadFilter(); f.type = "bandpass"; f.Q.value = 2;
    f.frequency.setValueAtTime(2200, t); f.frequency.exponentialRampToValueAtTime(450, t + 0.3);
    const g = ac.createGain();
    g.gain.setValueAtTime(0.4, t); g.gain.exponentialRampToValueAtTime(0.001, t + 0.35);
    src.connect(f).connect(g).connect(this.master);
    src.start(t); src.stop(t + 0.4);
    this.tone("sine", 320, 110, 0.25, 0.2);
  }
  pop() { if (this.ready) this.tone("sine", 220, 50, 0.25, 0.35); }
  thwip() {
    if (!this.ready) return;
    const ac = this.ac, t = ac.currentTime;
    const src = ac.createBufferSource(); src.buffer = this.noise;
    const f = ac.createBiquadFilter(); f.type = "highpass"; f.frequency.value = 2500;
    const g = ac.createGain();
    g.gain.setValueAtTime(0.35, t); g.gain.exponentialRampToValueAtTime(0.001, t + 0.12);
    src.connect(f).connect(g).connect(this.master);
    src.start(t); src.stop(t + 0.15);
    this.tone("triangle", 1800, 700, 0.08, 0.08);
  }
  whoosh() {
    if (!this.ready) return;
    const ac = this.ac, t = ac.currentTime;
    const src = ac.createBufferSource(); src.buffer = this.noise;
    const f = ac.createBiquadFilter(); f.type = "bandpass"; f.Q.value = 1.5;
    f.frequency.setValueAtTime(300, t); f.frequency.exponentialRampToValueAtTime(2500, t + 0.6);
    const g = ac.createGain();
    g.gain.setValueAtTime(0.5, t); g.gain.exponentialRampToValueAtTime(0.001, t + 0.8);
    src.connect(f).connect(g).connect(this.master);
    src.start(t); src.stop(t + 0.85);
  }
  get ready() { return !!this.ac; }
  setMuted(m) { if (this.ready) this.master.gain.setTargetAtTime(m ? 0 : 0.6, this.ac.currentTime, 0.02); }
  setBeam(level) {
    if (!this.ready) return;
    this.beamGain.gain.setTargetAtTime(Math.min(level, 3) * 0.06, this.ac.currentTime, 0.03);
  }
  setCharge(c) {
    if (!this.ready) return;
    const t = this.ac.currentTime;
    this.chargeGain.gain.setTargetAtTime(c > 0.01 ? 0.03 + c * 0.05 : 0, t, 0.05);
    this.chargeOsc.frequency.setTargetAtTime(180 + c * c * 1100, t, 0.05);
  }
  tone(type, f0, f1, dur, vol) {
    const ac = this.ac, t = ac.currentTime;
    const o = ac.createOscillator(); o.type = type;
    o.frequency.setValueAtTime(f0, t); o.frequency.exponentialRampToValueAtTime(f1, t + dur);
    const g = ac.createGain();
    g.gain.setValueAtTime(vol, t); g.gain.exponentialRampToValueAtTime(0.001, t + dur);
    o.connect(g).connect(this.master);
    o.start(t); o.stop(t + dur + 0.05);
  }
  transform() {
    if (!this.ready) return;
    this.blast(1);
    this.tone("sawtooth", 180, 1500, 0.7, 0.12);   // rising shriek over the boom
  }
  blast(power) {
    if (!this.ready) return;
    const ac = this.ac, t = ac.currentTime;
    const src = ac.createBufferSource(); src.buffer = this.noise;
    const f = ac.createBiquadFilter(); f.type = "lowpass";
    f.frequency.setValueAtTime(6000, t); f.frequency.exponentialRampToValueAtTime(150, t + 0.9);
    const g = ac.createGain();
    g.gain.setValueAtTime(0.5 * power + 0.2, t); g.gain.exponentialRampToValueAtTime(0.001, t + 1.0);
    src.connect(f).connect(g).connect(this.master);
    src.start(t); src.stop(t + 1.05);
    this.tone("sine", 140, 35, 0.6, 0.7 * power + 0.2);
  }
  hit() { if (this.ready) this.tone("square", 520, 90, 0.22, 0.15); }
  ko() {
    if (!this.ready) return;
    this.tone("sawtooth", 600, 40, 1.2, 0.2);
    this.tone("sine", 90, 25, 1.0, 0.6);
  }
}
export const sfx = new Sfx();

// Mic loudness for the Goku power-up: you have to yell to charge. The level is RMS in dBFS; the
// noise floor follows the quiet moments, and a yell is anything well above it.
const YELL_MARGIN = 28;   // dB above the room's noise floor that counts as a yell
const YELL_MIN = -40;     // ...and never quieter than this (dBFS)
const YELL_HOLD = 0.3;    // seconds a yell keeps counting through gaps between syllables

class Mic {
  constructor() { this.reset(); }
  reset() {
    this.read = null;   // returns the current level in dBFS; set by start() or by the simulator
    this.level = -100; this.floor = null; this.held = 0;
  }
  start(stream, ac) {
    const an = ac.createAnalyser();
    an.fftSize = 2048;
    const buf = new Float32Array(an.fftSize);
    ac.createMediaStreamSource(stream).connect(an);   // analysed only, never played back
    this.read = () => {
      an.getFloatTimeDomainData(buf);
      let sum = 0;
      for (const s of buf) sum += s * s;
      return 10 * Math.log10(sum / buf.length || 1e-10);
    };
  }
  update(dt) {
    if (!this.read) return;
    this.level = Math.max(-100, this.read());
    // floor drops quickly to quiet moments, and creeps up slowly (never while yelling) if the room gets louder
    if (this.floor === null) this.floor = this.level;
    else if (this.level < this.floor) this.floor += (this.level - this.floor) * Math.min(1, dt * 3);
    else if (this.level < this.threshold) this.floor += (this.level - this.floor) * Math.min(1, dt * 0.05);
    this.floor = clamp(this.floor, -90, -40);
    this.held = this.level > this.threshold ? YELL_HOLD : Math.max(0, this.held - dt);
  }
  get ready() { return !!this.read; }
  get threshold() { return Math.max(YELL_MIN, this.floor + YELL_MARGIN); }
  get yelling() { return this.held > 0; }
}
export const mic = new Mic();
