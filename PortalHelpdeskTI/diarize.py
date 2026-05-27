# diarize.py
# -*- coding: utf-8 -*-
import os, sys, json
from datetime import timedelta
from pyannote.audio import Pipeline

def fmt_hhmmss(seconds: float) -> str:
    s = int(seconds)
    hh = s // 3600
    mm = (s % 3600) // 60
    ss = s % 60
    return f"{hh:02d}:{mm:02d}:{ss:02d}"

def main():
    if len(sys.argv) < 3:
        print("usage: python diarize.py <audio.wav> <out.json>", file=sys.stderr)
        sys.exit(2)

    audio_path = sys.argv[1]
    out_path = sys.argv[2]

    token = os.environ.get("HUGGINGFACE_TOKEN")
    if not token:
        print("ERROR: HUGGINGFACE_TOKEN not set.", file=sys.stderr)
        sys.exit(3)

    # modelo de diarização
    pipeline = Pipeline.from_pretrained("pyannote/speaker-diarization", use_auth_token=token)

    diarization = pipeline(audio_path)

    out = []
    for turn, _, speaker in diarization.itertracks(yield_label=True):
        out.append({
            "speaker": speaker,                   # e.g. SPEAKER_00
            "start": float(turn.start),           # segundos
            "end": float(turn.end),               # segundos
            "start_hhmmss": fmt_hhmmss(turn.start),
            "end_hhmmss": fmt_hhmmss(turn.end),
        })

    with open(out_path, "w", encoding="utf-8") as f:
        json.dump({"speakers": out}, f, ensure_ascii=False, indent=2)

if __name__ == "__main__":
    main()
