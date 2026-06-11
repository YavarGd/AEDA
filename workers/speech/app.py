from __future__ import annotations

import os
import tempfile
from pathlib import Path

from fastapi import FastAPI, File, HTTPException, UploadFile
from faster_whisper import WhisperModel

app = FastAPI(title="PersonalAI Speech Worker")

MODEL_NAME = os.getenv("PERSONALAI_WHISPER_MODEL", "large-v3")
DEVICE = os.getenv("PERSONALAI_WHISPER_DEVICE", "cuda")
COMPUTE_TYPE = os.getenv(
    "PERSONALAI_WHISPER_COMPUTE_TYPE",
    "float16",
)

model = WhisperModel(
    MODEL_NAME,
    device=DEVICE,
    compute_type=COMPUTE_TYPE,
)


@app.get("/health")
def health() -> dict[str, str]:
    return {
        "status": "ok",
        "model": MODEL_NAME,
        "device": DEVICE,
        "compute_type": COMPUTE_TYPE,
    }


@app.post("/transcribe")
async def transcribe(
    audio: UploadFile = File(...),
) -> dict:
    suffix = Path(audio.filename or "audio.wav").suffix or ".wav"

    temp_path: str | None = None

    try:
        with tempfile.NamedTemporaryFile(
            delete=False,
            suffix=suffix,
        ) as temp_file:
            temp_path = temp_file.name
            temp_file.write(await audio.read())

        segments, info = model.transcribe(
            temp_path,
            beam_size=5,
            vad_filter=True,
        )

        segment_list = []
        text_parts = []

        for segment in segments:
            text_parts.append(segment.text.strip())
            segment_list.append(
                {
                    "start": segment.start,
                    "end": segment.end,
                    "text": segment.text.strip(),
                }
            )

        return {
            "text": " ".join(text_parts).strip(),
            "language": info.language,
            "language_probability": info.language_probability,
            "duration": info.duration,
            "segments": segment_list,
        }

    except Exception as exc:
        raise HTTPException(
            status_code=500,
            detail=str(exc),
        ) from exc

    finally:
        if temp_path and os.path.exists(temp_path):
            os.remove(temp_path)