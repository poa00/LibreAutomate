
using System.Drawing;

namespace Au.More
{
	/// <summary>
	/// Helps to get and release screen DC with the 'using(...){...}' pattern.
	/// Uses API GetDC and ReleaseDC.
	/// </summary>
	sealed class ScreenDC_ : IDisposable
	{
		IntPtr _dc;

		public ScreenDC_() => _dc = Api.GetDC(default);
		public static implicit operator IntPtr(ScreenDC_ dc) => dc._dc;
		public void Dispose() {
			if (_dc != default) {
				Api.ReleaseDC(default, _dc);
				_dc = default;
			}
		}
	}

	/// <summary>
	/// Helps to get and release window DC with the 'using(...){...}' pattern.
	/// Uses API GetDC and ReleaseDC.
	/// </summary>
	sealed class WindowDC_ : IDisposable, IDeviceContext
	{
		IntPtr _dc;
		wnd _w;

		public WindowDC_(IntPtr dc, wnd w) { _dc = dc; _w = w; }

		public WindowDC_(wnd w) => _dc = Api.GetDC(_w = w);

		public static implicit operator IntPtr(WindowDC_ dc) => dc._dc;

		public bool Is0 => _dc == default;

		public void Dispose() {
			if (_dc != default) {
				Api.ReleaseDC(_w, _dc);
				_dc = default;
			}
		}

		IntPtr IDeviceContext.GetHdc() => _dc;

		void IDeviceContext.ReleaseHdc() => Dispose();
	}

	/// <summary>
	/// Helps to create and delete compatible DC (memory DC) with the 'using(...){...}' pattern.
	/// Uses API CreateCompatibleDC and DeleteDC.
	/// </summary>
	class MemoryDC_ : IDisposable, IDeviceContext
	{
		protected IntPtr _dc;

		/// <summary>
		/// Creates memory DC compatible with screen.
		/// </summary>
		public MemoryDC_() : this(default) { }

		public MemoryDC_(IntPtr dc) => _dc = Api.CreateCompatibleDC(dc);

		public static implicit operator IntPtr(MemoryDC_ dc) => dc._dc;

		public bool Is0 => _dc == default;

		public void Dispose() => Dispose(true);

		protected virtual void Dispose(bool disposing) {
			if (_dc != default) {
				Api.DeleteDC(_dc);
				_dc = default;
			}
		}

		IntPtr IDeviceContext.GetHdc() => _dc;

		void IDeviceContext.ReleaseHdc() => Dispose();
	}

	/// <summary>
	/// Memory DC with selected font.
	/// Can be used for font measurement.
	/// </summary>
	sealed class FontDC_ : MemoryDC_
	{
		IntPtr _oldFont;

		/// <summary>
		/// Selects specified font.
		/// The <b>Dispose</b> method will select it out but will not destroy it.
		/// </summary>
		/// <param name="font"></param>
		public FontDC_(IntPtr font) {
			_oldFont = Api.SelectObject(_dc, font);
		}

		/// <summary>
		/// Selects standard UI font for specified DPI.
		/// </summary>
		/// <param name="dpi"></param>
		public FontDC_(DpiOf dpi) : this(NativeFont_.RegularCached(dpi)) { }

		protected override void Dispose(bool disposing) {
			if (_oldFont != default) {
				Api.SelectObject(_dc, _oldFont);
				_oldFont = default;
			}
			base.Dispose(disposing);
		}

		/// <summary>
		/// Measures text with API <msdn>GetTextExtentPoint32</msdn>.
		/// Should be single line without tabs. For drawing with API <msdn>TextOut</msdn> or <msdn>ExtTextOut</msdn>.
		/// </summary>
		public SIZE Measure(string s) {
			Api.GetTextExtentPoint32(_dc, s, s.Length, out var z);
			return z;
		}

		/// <summary>
		/// Measures text with API <msdn>DrawText</msdn>.
		/// Can be multiline. For drawing with API <msdn>DrawText</msdn>.
		/// </summary>
		public SIZE Measure(string s, int length, TFFlags format, int wrapWidth = 0) {
			if ((uint)length > s.Lenn()) throw new ArgumentException();
			if (length == 0) return default;
			RECT r = new(0, 0, wrapWidth, 0);
			Api.DrawText(_dc, s, length, ref r, format | TFFlags.CALCRECT);
			return new(r.Width, r.Height);
		}

		/// <summary>
		/// Measures text with API <msdn>DrawText</msdn>.
		/// Can be multiline. For drawing with API <msdn>DrawText</msdn>.
		/// </summary>
		public SIZE Measure(string s, TFFlags format, int wrapWidth = 0) => Measure(s, s.Lenn(), format, wrapWidth);
	}

	struct Pen_ : IDisposable
	{
		IntPtr _pen;

		public IntPtr Handle => _pen;

		public Pen_(int color, int width = 1, int style = 0) {
			_pen = Api.CreatePen(style, width, color);
		}

		public void Dispose() {
			Api.DeleteObject(_pen);
			_pen = default;
		}

		/// <summary>
		/// Draws line and returns previous "current position".
		/// Don't need to select pen into DC.
		/// </summary>
		public POINT DrawLine(IntPtr dc, POINT start, POINT end) {
			var old = Api.SelectObject(dc, _pen); //fast
			Api.MoveToEx(dc, start.x, start.y, out var p); //fast
			Api.LineTo(dc, end.x, end.y);
			Api.SelectObject(dc, old); //fast
			return p;
		}
	}

	struct GdiSelectObject_ : IDisposable
	{
		IntPtr _dc, _old;

		public GdiSelectObject_(IntPtr dc, IntPtr obj) {
			_old = Api.SelectObject(_dc = dc, obj);
		}

		public void Dispose() {
			Api.SelectObject(_dc, _old);
			_dc = default;
		}
	}
}
