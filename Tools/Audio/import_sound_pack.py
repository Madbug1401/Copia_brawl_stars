"""Prepare the supplied pack without changing its originals. Requires numpy.

Usage: python Tools/Audio/import_sound_pack.py <source directory>
Outputs mono PCM16 WAVs, stable Unity importer metadata and an audit manifest.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import wave
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / 'Assets/_Game/Resources/Audio/Sfx'
# sound, original relative path, output folder, windows in source seconds,
# output peak ceiling, mixer volume, pitch variation. Windows exclude later
# shots/explosions, so gameplay continues to schedule those events itself.
PACK = [
    ('Shot', 'armas/REVOLVER.wav', 'Armas', [(0.08, 0.48), (0.48, 1.22)], .71, .85, .025),
    ('Shotgun', 'armas/SHOTGUN.wav', 'Armas', [(0.025, 1.25)], .71, .85, .02),
    ('Sniper', 'armas/SNIPER.wav', 'Armas', [(0.19, 1.95)], .71, .85, .015),
    ('Smg', 'armas/SUBMATRELHADORA.wav', 'Armas', [(0.04, .40)], .71, .78, .025),
    ('MinigunSpin', 'armas/Minigun_Sound_Test_SFM.wav', 'Armas', [(1.79, 2.19)], .63, .65, .01),
    ('Minigun', 'armas/Minigun_Sound_Test_SFM.wav', 'Armas', [(2.22, 2.51), (2.60, 2.89), (3.20, 3.49)], .71, .72, .015),
    ('PoisonHiss', 'explosivos e armadilhas/ARMADILHA GAZ VENENO.wav', 'ExplosivosArmadilhas', [(0.47, 2.10)], .63, .65, .025),
    ('MineArm', 'explosivos e armadilhas/ARMAR MINA.wav', 'ExplosivosArmadilhas', [(0.06, .26)], .63, .65, .015),
    ('Explosion', 'explosivos e armadilhas/EXPLOSAO.wav', 'ExplosivosArmadilhas', [(0.14, 3.50)], .71, .72, .035),
    ('Hit', 'impactos e combates/ACERTO NO CORPO.wav', 'ImpactosCombate', [(0.11, .50)], .56, .65, 0),
    ('Crate', 'impactos e combates/CAIXA A PARTIR.wav', 'ImpactosCombate', [(0.05, .85)], .63, .75, .04),
    ('Kill', 'impactos e combates/ELIMINACAO.wav', 'ImpactosCombate', [(0, 2.30)], .56, .65, 0),
    ('HeadHit', 'impactos e combates/HEADSHOT.wav', 'ImpactosCombate', [(0.05, .52)], .56, .72, 0),
    ('FatalShot', 'impactos e combates/TIRO FATAL.wav', 'ImpactosCombate', [(0.21, 1.18)], .71, .85, .01),
    ('Empty', 'manuseamento/GATILHO SEM MUNICAO.wav', 'Manuseamento', [(0.10, .28)], .50, .65, .015),
    ('Reload', 'manuseamento/RECARGA.wav', 'Manuseamento', [(0.46, 1.70)], .56, .60, .015),
    ('Bow', 'outros ataques/ARCO E FLECHA.wav', 'OutrosAtaques', [(0.01, .70)], .63, .80, .03),
    ('Melee', 'outros ataques/GOLPE DE CORPO.wav', 'OutrosAtaques', [(0.09, .44)], .63, .80, .04),
    ('Lob', 'outros ataques/Grenade_Sound_Effect_Free_Fire_Whatsapp_Status_Grenade_Bomb_Sound_Effect_shorts.wav', 'OutrosAtaques', [(0.15, .45)], .56, .75, .03),
    ('Spray', 'outros ataques/SPRAY.wav', 'OutrosAtaques', [(0.02, .48)], .63, .75, .025),
    ('Step', 'personagem/Footsteps_Grass_Sound_Effect_HD.wav', 'Personagem', [(0.08,.36),(.70,.98),(1.32,1.60),(1.96,2.24),(2.62,2.90),(2.96,3.20),(3.32,3.60),(3.98,4.27)], .56, .85, .06),
    ('Death', 'personagem/MORTE.wav', 'Personagem', [(0.06, 1.50)], .56, .80, .025),
    ('Hurt', 'personagem/REACAO DOR.wav', 'Personagem', [(0.16, .65)], .56, .70, .03),
    ('Unlock', 'recompensa/DESBLOQUEAR.wav', 'Recompensas', [(0.08, .65)], .50, .75, 0),
    ('Reward', 'recompensa/RECEBER RECOMPENSA.wav', 'Recompensas', [(0.05, .44)], .50, .70, 0),
    ('LevelUp', 'recompensa/SUBIR NIVEL.wav', 'Recompensas', [(0.17, .56)], .50, .80, 0),
]

def read_wav(path):
    # Downloads include float WAV and RIFF/data sizes left at 0xffffffff.
    # Bound chunks by actual file size, then rewrite a canonical PCM header.
    raw = path.read_bytes()
    if raw[:4] != b'RIFF' or raw[8:12] != b'WAVE':
        raise ValueError(f'Not a RIFF WAV: {path}')
    pos = 12
    fmt = data = None
    while pos + 8 <= len(raw):
        tag, length = struct.unpack_from('<4sI', raw, pos)
        start = pos + 8
        if tag == b'fmt ': fmt = struct.unpack_from('<HHIIHH', raw, start)
        if tag == b'data':
            data = raw[start:min(start + length, len(raw))]
            break
        pos = start + length + length % 2
    if fmt is None or data is None: raise ValueError(f'Incomplete WAV: {path}')
    code, channels, rate, _, align, bits = fmt
    dtype = {(1,16): '<i2', (1,32): '<i4', (3,32): '<f4'}[(code,bits)]
    samples = np.frombuffer(data[:len(data)//align*align], dtype=dtype).astype(np.float64).reshape(-1,channels)
    if code == 1: samples /= 2 ** (bits - 1)
    samples = samples.mean(axis=1)
    if not np.isfinite(samples).all(): raise ValueError(f'Non-finite samples: {path}')
    return rate, samples

def guid(path):
    return hashlib.md5(str(path.relative_to(ROOT)).replace('\\','/').encode()).hexdigest()

def metadata(path, folder=False):
    meta = Path(str(path) + '.meta')
    if meta.exists(): return
    header = f'fileFormatVersion: 2\nguid: {guid(path)}\n'
    if folder:
        text = header + 'folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
    else:
        text = header + '''AudioImporter:
  externalObjects: {}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 48000
    compressionFormat: 0
    quality: 1
    conversionMode: 0
    preloadAudioData: 1
  platformSettingOverrides: {}
  forceToMono: 1
  normalize: 0
  loadInBackground: 0
  ambisonic: 0
  3D: 1
  userData: FPS-LAN prepared SFX
  assetBundleName: 
  assetBundleVariant: 
'''
    meta.write_text(text, encoding='utf-8')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    args = parser.parse_args()
    cues = []
    for sound, source, category, windows, ceiling, volume, pitch in PACK:
        original = args.source / source
        rate, samples = read_wav(original)
        segments = [samples[round(a*rate):round(b*rate)].copy() for a,b in windows]
        peak = max(float(np.max(np.abs(x))) for x in segments)
        gain = min(ceiling / max(peak, 1e-9), 8.0)
        clips = []
        for i, (segment, window) in enumerate(zip(segments, windows),1):
            # Short fades eliminate discontinuities on extracted burst edges.
            fade_in = min(round(rate*.002), len(segment)//2)
            fade_out = min(round(rate*(.015 if sound == 'Minigun' else .008)),len(segment)//2)
            segment *= gain
            segment[:fade_in] *= np.linspace(0,1,fade_in)
            segment[-fade_out:] *= np.linspace(1,0,fade_out)
            name = sound if len(segments)==1 else f'{sound}_{i:02d}'
            target = DEST / category / f'{name}.wav'
            target.parent.mkdir(parents=True,exist_ok=True)
            with wave.open(str(target),'wb') as w:
                w.setnchannels(1); w.setsampwidth(2); w.setframerate(rate)
                w.writeframes(np.round(np.clip(segment,-1,1)*32767).astype('<i2').tobytes())
            metadata(target)
            clips.append({'asset':str(target.relative_to(ROOT)).replace('\\','/'), 'sourceWindow':window, 'seconds':len(segment)/rate})
        cues.append({'sound':sound,'source':source,'sourceSha256':hashlib.sha256(original.read_bytes()).hexdigest(),'gain':round(gain,5),'volume':volume,'pitchVariation':pitch,'clips':clips})
    for folder in [DEST, *sorted(p for p in DEST.rglob('*') if p.is_dir())]: metadata(folder,folder=True)
    (ROOT/'Tools/Audio/sound_pack_manifest.json').write_text(json.dumps({'cues':cues},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'Prepared {len(cues)} cues / {sum(len(c["clips"]) for c in cues)} clips from {len(set(c["source"] for c in cues))} source files.')

if __name__ == '__main__': main()
