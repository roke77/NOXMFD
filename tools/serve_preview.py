#!/usr/bin/env python3
"""Backward-compatible wrapper for the old preview server command.

The preview now serves the real src/web/ frontend through tools/serve_web.py. This
wrapper keeps `python tools/serve_preview.py` usable for existing notes and
launch habits, defaulting to the old 8777 port unless --port is supplied.
"""
import os
import sys


os.environ.setdefault("PORT", "8777")
sys.path.insert(0, os.path.dirname(__file__))

from serve_web import main


if __name__ == "__main__":
    main()
