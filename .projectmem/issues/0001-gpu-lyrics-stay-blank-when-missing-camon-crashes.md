# #0001 GPU lyrics stay blank when missing camOn crashes buildGpuLine; ticker Text exceeds WebGPU texture limit

- 2026-11-24T11:41:12Z `issue`: GPU lyrics stay blank when missing camOn crashes buildGpuLine; ticker Text exceeds WebGPU texture limit [KineticWallpaper/WebContent/index.html:838]
- 2026-11-24T11:41:24Z `attempt`: Replaced undefined camera helper with camera mode check; bounded repeated ticker texture and kept lyrics lookup status visible. Release build and inline JS syntax pass; runtime display not yet confirmed. [KineticWallpaper/WebContent/index.html:838] (partial)
- 2026-11-24T11:43:36Z `attempt`: Bound Pixi ticker texture to 4000 logical pixels, reset base font on each track and viewport layout, and removed ignored powerPreference. Release build and inline JavaScript syntax pass; live WebView2 rendering remains unverified. [KineticWallpaper/WebContent/index.html:127] (partial)
