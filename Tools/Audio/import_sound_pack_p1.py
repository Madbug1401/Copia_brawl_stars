"""Pacote P1 (26/10/2026, Djavan): prepara os WAVs sem alterar os originais. Requer numpy e ffmpeg.

Uso: python Tools/Audio/import_sound_pack_p1.py "<pasta Sons em Falta>"
Efeitos curtos -> Resources/Audio/Sfx (PCM16 mono 48 kHz). Ambientes -> Resources/Audio/Ambience (loops).
Escreve Tools/Audio/sound_pack_p1_manifest.json (lido por BuildSoundCatalog.cs junto com o manifesto original).
"""
import argparse, hashlib, json, subprocess, wave
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SFX = ROOT / 'Assets/_Game/Resources/Audio/Sfx'
AMB = ROOT / 'Assets/_Game/Resources/Audio/Ambience'
RATE = 48000

# som, ficheiro original, pasta, janelas (s), teto de pico, volume no catálogo, variação de tom, fade-out (s)
PACK = [
    ('SuperReady',      'super caregado/mixkit-game-success-alert-2039.wav', 'Habilidades', [(0.0, 1.0)], .56, .80, 0, .25),
    ('MatchEnd',        'game finished/mixkit-fantasy-game-sweep-notification-255.wav', 'Partida', [(0.0, 1.6)], .56, .80, 0, .40),
    ('FinalTick',       'Ultimos 10 segundos - tick por segundo/mixkit-tick-tock-clock-close-up-1059.wav', 'Partida', [(0.57, 0.85), (1.07, 1.35)], .50, .75, 0, .05),
    ('LowHealthBeat',   'batimento cardiaco com vida baixa/mixkit-human-single-heart-beat-490.wav', 'Partida', [(0.0, 0.6)], .63, .85, 0, .08),
    ('GemSpawn',        'Gerador central cria uma gema (substitui o/mixkit-long-pop-2358.wav', 'GemRush', [(0.0, 0.45)], .56, .70, .03, .05),
    ('GemPickup',       'Tu ou um aliado apanha uma gema/mixkit-soap-bubble-sound-2925.wav', 'GemRush', [(0.0, 0.5)], .56, .75, .04, .10),
    ('GemCountdown',    'Equipa atinge o objetivo de gemas comeca a/mixkit-fantasy-game-success-notification-270.wav', 'GemRush', [(0.0, 1.8)], .56, .80, 0, .30),
    ('PetFoodDenied',   'UPRA sem municao/mixkit-video-game-mystery-alert-234.wav', 'Pets', [(0.0, 0.43)], .50, .65, 0, .05),
    ('SuperEkko',       'super sfx/echo.wav', 'Habilidades', [(0.0, 0.176)], .63, .80, .02, .02),
    ('SuperLince',      'super sfx/lins.wav', 'Habilidades', [(0.0, 0.30)], .63, .80, .02, .04),
    ('SuperRiot',       'super sfx/riot.wav', 'Habilidades', [(0.0, 1.6)], .63, .85, .02, .35),
    ('SuperMrS',        'super sfx/MS.wav', 'Habilidades', [(0.0, 0.45)], .63, .80, .02, .05),
    ('SuperLupus',      'super sfx/plus marinus.wav', 'Habilidades', [(0.12, 1.4)], .63, .85, .02, .25),
    ('SuperNCage',      'super sfx/N Cage.wav', 'Habilidades', [(0.0, 0.38)], .63, .80, .02, .04),
]
# nome do loop, original, canais, crossfade (s), teto
AMBIENCE = [
    ('LavaBubble_loop', 'ambiente/lava sound/mixkit-bubbling-volcano-lava-flow-2444.wav', 1, 1.5, .50),
    ('VolcanoInterior_loop', 'ambiente/momentum/mixkit-inside-a-volcano-2438.wav', 2, 1.2, .50),
]

def load(path, channels):
    raw = subprocess.run(['ffmpeg', '-v', 'error', '-i', str(path), '-ac', str(channels), '-ar', str(RATE), '-f', 'f64le', '-'],
                         capture_output=True, check=True).stdout
    x = np.frombuffer(raw, np.float64).reshape(-1, channels).copy()
    if not np.isfinite(x).all(): raise ValueError(f'Non-finite samples: {path}')
    return x

def write(target, x):
    target.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(target), 'wb') as w:
        w.setnchannels(x.shape[1]); w.setsampwidth(2); w.setframerate(RATE)
        w.writeframes(np.round(np.clip(x, -1, 1) * 32767).astype('<i2').tobytes())

def guid(path):
    return hashlib.md5(str(path.relative_to(ROOT)).replace('\\', '/').encode()).hexdigest()

def meta(path, folder=False):
    m = Path(str(path) + '.meta')
    if m.exists(): return
    head = f'fileFormatVersion: 2\nguid: {guid(path)}\n'
    if folder:
        m.write_text(head + 'folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n', encoding='utf-8')
    else:
        m.write_text(head + 'AudioImporter:\n  externalObjects: {}\n  serializedVersion: 8\n  userData: FPS-LAN pack P1\n  assetBundleName: \n  assetBundleVariant: \n', encoding='utf-8')

def main():
    ap = argparse.ArgumentParser(description=__doc__); ap.add_argument('source', type=Path); a = ap.parse_args()
    cues = []
    for sound, src, folder, windows, ceiling, volume, pitch, fade in PACK:
        x = load(a.source / src, 1)[:, 0]
        segs = [x[round(s * RATE):round(e * RATE)].copy() for s, e in windows]
        gain = min(ceiling / max(max(float(np.abs(s).max()) for s in segs), 1e-9), 8.0)
        clips = []
        for i, (seg, win) in enumerate(zip(segs, windows), 1):
            seg *= gain
            fi = min(round(RATE * .002), len(seg) // 2); fo = min(round(RATE * fade), len(seg) // 2)
            seg[:fi] *= np.linspace(0, 1, fi); seg[-fo:] *= np.linspace(1, 0, fo) ** 2
            name = sound if len(segs) == 1 else f'{sound}_{i:02d}'
            t = SFX / folder / f'{name}.wav'; write(t, seg[:, None]); meta(t)
            clips.append({'asset': str(t.relative_to(ROOT)).replace('\\', '/'), 'sourceWindow': win, 'seconds': round(len(seg) / RATE, 4)})
        cues.append({'sound': sound, 'source': src, 'sourceSha256': hashlib.sha256((a.source / src).read_bytes()).hexdigest(),
                     'gain': round(gain, 5), 'volume': volume, 'pitchVariation': pitch, 'clips': clips})
    loops = []
    for name, src, ch, xf, ceiling in AMBIENCE:
        x = load(a.source / src, ch)
        n = round(xf * RATE)
        # loop sem clique: o fim faz crossfade (potência constante) para o início
        body, head, tail = x[n:-n] if len(x) > 3 * n else x, x[:n], x[-n:]
        k = np.linspace(0, np.pi / 2, n)[:, None]
        loop = body.copy(); loop[-n:] = loop[-n:] * np.cos(k) + head * np.sin(k)
        loop *= ceiling / max(float(np.abs(loop).max()), 1e-9)
        t = AMB / f'{name}.wav'; write(t, loop); meta(t)
        loops.append({'asset': str(t.relative_to(ROOT)).replace('\\', '/'), 'source': src, 'channels': ch, 'seconds': round(len(loop) / RATE, 3)})
    for f in [SFX, AMB, *sorted(p for d in (SFX, AMB) for p in d.rglob('*') if p.is_dir())]: meta(f, folder=True)
    (ROOT / 'Tools/Audio/sound_pack_p1_manifest.json').write_text(json.dumps({'cues': cues, 'ambience': loops}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'P1: {len(cues)} cues / {sum(len(c["clips"]) for c in cues)} clips, {len(loops)} ambient loops.')

if __name__ == '__main__': main()
