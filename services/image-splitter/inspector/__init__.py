"""
NSCIM X-Ray Inspector — pure-Python analysis backend.

Exposes a FastAPI router that decodes raw scanner blobs (ASE from
`asescans.scanimage`, FS6000 from `.img` files on the file share) without
any vendor DLL, and provides a catalog of image-analysis operations
(window/level, histograms, edge/threshold/object detection, dual-energy
compositing, material discrimination) to the NSCIM Blazor `X-Ray Inspector`
page.

Mount in `main.py`:

    from inspector.routes import router as inspector_router
    app.include_router(inspector_router)

Default output bit depth follows the project-wide rule recorded in
`feedback_xray_rendering_raw16.md`: 16-bit PNG for 16-bit channels
(ASE main, FS6000 high/low), native 8-bit for FS6000 material. Cooked
8-bit transforms are opt-in only.
"""
