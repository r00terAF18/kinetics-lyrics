package com.kineticslyrics.app;

public final class KineticLyricsWallpaperService extends android.service.wallpaper.WallpaperService {
	@Override
	public Engine onCreateEngine ()
	{
		return new LyricsEngine ();
	}

	public final class LyricsEngine extends Engine {
		private final KineticLyricsWallpaperController controller;

		LyricsEngine ()
		{
			KineticLyricsWallpaperService.this.super ();
			setTouchEventsEnabled (false);
			controller = new KineticLyricsWallpaperController ();
		}

		@Override
		public void onVisibilityChanged (boolean visible)
		{
			super.onVisibilityChanged (visible);
			controller.onVisibilityChanged (visible);
		}

		@Override
		public void onSurfaceChanged (android.view.SurfaceHolder holder, int format, int width, int height)
		{
			super.onSurfaceChanged (holder, format, width, height);
			controller.onSurfaceChanged (holder, format, width, height);
		}

		@Override
		public void onSurfaceDestroyed (android.view.SurfaceHolder holder)
		{
			controller.onSurfaceDestroyed (holder);
			super.onSurfaceDestroyed (holder);
		}

		@Override
		public void onDestroy ()
		{
			controller.onDestroy ();
			super.onDestroy ();
		}
	}
}
