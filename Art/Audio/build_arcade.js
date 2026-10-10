/*
 * Renders the Arcade sound set: every jsfxr preset in Art/Audio/Arcade/ becomes a WAV of the
 * same name in Assets/Audio/Arcade/.
 *
 *     cd Art/Audio
 *     npm install            # once: downloads jsfxr into node_modules/ (not committed)
 *     node build_arcade.js
 *
 * jsfxr (https://sfxr.me) is the classic "8-bit sound effect" generator. A sound is described
 * by a couple of dozen numbers (the preset): a waveform, a volume envelope, a starting pitch,
 * how the pitch slides or jumps, and so on. The same numbers always make the same sound, so the
 * presets are the source files and the WAVs can be rebuilt from them at any time.
 *
 * To tweak a sound by ear, open https://sfxr.me, paste the preset's JSON into the box under
 * "Serialize / Deserialize", adjust the sliders, then copy the JSON back into the file and run
 * this script again.
 */

const fs = require("fs");
const path = require("path");
const { sfxr } = require("jsfxr");

const PRESETS = path.join(__dirname, "Arcade");
const OUTPUT = path.join(__dirname, "..", "..", "Assets", "Audio", "Arcade");

fs.mkdirSync(OUTPUT, { recursive: true });

for (const file of fs.readdirSync(PRESETS).sort()) {
  if (!file.endsWith(".json")) {
    continue;
  }
  const preset = JSON.parse(fs.readFileSync(path.join(PRESETS, file), "utf8"));

  // toWave runs the synthesizer and returns a whole .wav file (header and samples) as an
  // array of bytes, in the sample rate and bit depth the preset asks for.
  const wave = sfxr.toWave(preset);
  const target = path.join(OUTPUT, file.replace(/\.json$/, ".wav"));
  fs.writeFileSync(target, Buffer.from(wave.wav));

  const seconds = wave.header.subChunk2Size / wave.header.byteRate;
  // "clipping" counts samples that were too loud to store and got cut off flat, which sounds
  // harsh. If it isn't 0, lower the preset's sound_vol.
  console.log(`${path.basename(target)}: ${seconds.toFixed(2)}s, ${wave.clipping} clipped samples`);
}
