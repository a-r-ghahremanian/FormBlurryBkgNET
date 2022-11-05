using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace FormBlurryBkgNET
{
    public class frmFormBlurryBkgNET : Form
    {

        #region Classes / Blur

        public class GaussianBlur
        {

            private readonly int[] _alpha;
            private readonly int[] _red;
            private readonly int[] _green;
            private readonly int[] _blue;

            private readonly int _width;
            private readonly int _height;

            private readonly ParallelOptions _pOptions = new ParallelOptions { MaxDegreeOfParallelism = 16 };

            public GaussianBlur(Bitmap image)
            {
                var rct = new Rectangle(0, 0, image.Width, image.Height);
                var source = new int[rct.Width * rct.Height];
                var bits = image.LockBits(rct, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                Marshal.Copy(bits.Scan0, source, 0, source.Length);
                image.UnlockBits(bits);

                _width = image.Width;
                _height = image.Height;

                _alpha = new int[_width * _height];
                _red = new int[_width * _height];
                _green = new int[_width * _height];
                _blue = new int[_width * _height];

                Parallel.For(0, source.Length, _pOptions, i =>
                {
                    _alpha[i] = (int)((source[i] & 0xff000000) >> 24);
                    _red[i] = (source[i] & 0xff0000) >> 16;
                    _green[i] = (source[i] & 0x00ff00) >> 8;
                    _blue[i] = (source[i] & 0x0000ff);
                });
            }

            public Bitmap Process(int BlurAmount)
            {
                var newAlpha = new int[_width * _height];
                var newRed = new int[_width * _height];
                var newGreen = new int[_width * _height];
                var newBlue = new int[_width * _height];
                var dest = new int[_width * _height];

                Parallel.Invoke(
                    () => gaussBlur_4(_alpha, newAlpha, BlurAmount),
                    () => gaussBlur_4(_red, newRed, BlurAmount),
                    () => gaussBlur_4(_green, newGreen, BlurAmount),
                    () => gaussBlur_4(_blue, newBlue, BlurAmount));

                Parallel.For(0, dest.Length, _pOptions, i =>
                {
                    if (newAlpha[i] > 255) newAlpha[i] = 255;
                    if (newRed[i] > 255) newRed[i] = 255;
                    if (newGreen[i] > 255) newGreen[i] = 255;
                    if (newBlue[i] > 255) newBlue[i] = 255;

                    if (newAlpha[i] < 0) newAlpha[i] = 0;
                    if (newRed[i] < 0) newRed[i] = 0;
                    if (newGreen[i] < 0) newGreen[i] = 0;
                    if (newBlue[i] < 0) newBlue[i] = 0;

                    dest[i] = (int)((uint)(newAlpha[i] << 24) | (uint)(newRed[i] << 16) | (uint)(newGreen[i] << 8) | (uint)newBlue[i]);
                });

                var image = new Bitmap(_width, _height);
                var rct = new Rectangle(0, 0, image.Width, image.Height);
                var bits2 = image.LockBits(rct, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                Marshal.Copy(dest, 0, bits2.Scan0, dest.Length);
                image.UnlockBits(bits2);
                return image;
            }

            private void gaussBlur_4(int[] source, int[] dest, int r)
            {
                var bxs = boxesForGauss(r, 3);
                boxBlur_4(source, dest, _width, _height, (bxs[0] - 1) / 2);
                boxBlur_4(dest, source, _width, _height, (bxs[1] - 1) / 2);
                boxBlur_4(source, dest, _width, _height, (bxs[2] - 1) / 2);
            }

            private int[] boxesForGauss(int sigma, int n)
            {
                var wIdeal = Math.Sqrt((12 * sigma * sigma / n) + 1);
                var wl = (int)Math.Floor(wIdeal);
                if (wl % 2 == 0) wl--;
                var wu = wl + 2;

                var mIdeal = (double)(12 * sigma * sigma - n * wl * wl - 4 * n * wl - 3 * n) / (-4 * wl - 4);
                var m = Math.Round(mIdeal);

                var sizes = new List<int>();
                for (var i = 0; i < n; i++) sizes.Add(i < m ? wl : wu);
                return sizes.ToArray();
            }

            private void boxBlur_4(int[] source, int[] dest, int w, int h, int r)
            {
                for (var i = 0; i < source.Length; i++) dest[i] = source[i];
                boxBlurH_4(dest, source, w, h, r);
                boxBlurT_4(source, dest, w, h, r);
            }

            private void boxBlurH_4(int[] source, int[] dest, int w, int h, int r)
            {
                var iar = (double)1 / (r + r + 1);
                Parallel.For(0, h, _pOptions, i =>
                {
                    var ti = i * w;
                    var li = ti;
                    var ri = ti + r;
                    var fv = source[ti];
                    var lv = source[ti + w - 1];
                    var val = (r + 1) * fv;
                    for (var j = 0; j < r; j++) val += source[ti + j];
                    for (var j = 0; j <= r; j++)
                    {
                        val += source[ri++] - fv;
                        dest[ti++] = (int)Math.Round(val * iar);
                    }
                    for (var j = r + 1; j < w - r; j++)
                    {
                        val += source[ri++] - dest[li++];
                        dest[ti++] = (int)Math.Round(val * iar);
                    }
                    for (var j = w - r; j < w; j++)
                    {
                        val += lv - source[li++];
                        dest[ti++] = (int)Math.Round(val * iar);
                    }
                });
            }

            private void boxBlurT_4(int[] source, int[] dest, int w, int h, int r)
            {
                var iar = (double)1 / (r + r + 1);
                Parallel.For(0, w, _pOptions, i =>
                {
                    var ti = i;
                    var li = ti;
                    var ri = ti + r * w;
                    var fv = source[ti];
                    var lv = source[ti + w * (h - 1)];
                    var val = (r + 1) * fv;
                    for (var j = 0; j < r; j++) val += source[ti + j * w];
                    for (var j = 0; j <= r; j++)
                    {
                        val += source[ri] - fv;
                        dest[ti] = (int)Math.Round(val * iar);
                        ri += w;
                        ti += w;
                    }
                    for (var j = r + 1; j < h - r; j++)
                    {
                        val += source[ri] - source[li];
                        dest[ti] = (int)Math.Round(val * iar);
                        li += w;
                        ri += w;
                        ti += w;
                    }
                    for (var j = h - r; j < h; j++)
                    {
                        val += lv - source[li];
                        dest[ti] = (int)Math.Round(val * iar);
                        li += w;
                        ti += w;
                    }
                });
            }

        }

        #endregion

        #region CTor
        public frmFormBlurryBkgNET()
        {
            #region Init
            this.SuspendLayout();
            
            this.ClientSize = new System.Drawing.Size(800, 600);
            this.Name = "frmFormBlurryBkgNET";
            this.Text = "frmFormBlurryBkgNET";

            this.AllowTransparency = true;
            this.Opacity = 0.975;

            this.Move += (_, __) => { if (ModifierKeys.HasFlag(Keys.Control)) GetBkgImgAndBlurIt(1, UseZeroOpacity: ModifierKeys.HasFlag(Keys.Shift)); };
            
            this.ResumeLayout(false);
            #endregion

            GetBkgImgAndBlurIt();
        }

        #endregion



        #region API / Types, Structs
        
        public enum TernaryRasterOperations : uint
        {
            SRCCOPY = 0x00CC0020,
            SRCPAINT = 0x00EE0086,
            SRCAND = 0x008800C6,
            SRCINVERT = 0x00660046,
            SRCERASE = 0x00440328,
            NOTSRCCOPY = 0x00330008,
            NOTSRCERASE = 0x001100A6,
            MERGECOPY = 0x00C000CA,
            MERGEPAINT = 0x00BB0226,
            PATCOPY = 0x00F00021,
            PATPAINT = 0x00FB0A09,
            PATINVERT = 0x005A0049,
            DSTINVERT = 0x00550009,
            BLACKNESS = 0x00000042,
            WHITENESS = 0x00FF0062,
            CAPTUREBLT = 0x40000000 //only if WinVer >= 5.0.0 (see wingdi.h)
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        #endregion

        #region API / Methods

        /// <summary>
        ///    Performs a bit-block transfer of the color data corresponding to a
        ///    rectangle of pixels from the specified source device context into
        ///    a destination device context.
        /// </summary>
        /// <param name="hdc">Handle to the destination device context.</param>
        /// <param name="nXDest">The leftmost x-coordinate of the destination rectangle (in pixels).</param>
        /// <param name="nYDest">The topmost y-coordinate of the destination rectangle (in pixels).</param>
        /// <param name="nWidth">The width of the source and destination rectangles (in pixels).</param>
        /// <param name="nHeight">The height of the source and the destination rectangles (in pixels).</param>
        /// <param name="hdcSrc">Handle to the source device context.</param>
        /// <param name="nXSrc">The leftmost x-coordinate of the source rectangle (in pixels).</param>
        /// <param name="nYSrc">The topmost y-coordinate of the source rectangle (in pixels).</param>
        /// <param name="dwRop">A raster-operation code.</param>
        /// <returns>
        ///    <c>true</c> if the operation succeedes, <c>false</c> otherwise. To get extended error information, call <see cref="System.Runtime.InteropServices.Marshal.GetLastWin32Error"/>.
        /// </returns>
        [DllImport("gdi32.dll", EntryPoint = "BitBlt", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool BitBlt([In] IntPtr hdc, int nXDest, int nYDest, int nWidth, int nHeight, [In] IntPtr hdcSrc, int nXSrc, int nYSrc, TernaryRasterOperations dwRop);


        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern IntPtr ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowRect(IntPtr hWnd, ref RECT rect);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);


        #endregion


        #region Methods / ScreenCapture

        public Rectangle GetRectangleForCapture()
        {
            //var AllScreenW = Screen.AllScreens.Sum(x => x.WorkingArea.Size.Width);
            //var AllScreenH = Screen.AllScreens.Max(x => x.WorkingArea.Size.Height);
            var BackImg4UseRectWdth = 0;
            var BackImg4UseRectHght = 0;
            for (int i = 0; i < Screen.AllScreens.Length; i++)
            {
                BackImg4UseRectWdth += Screen.AllScreens[i].WorkingArea.Width;
                if (BackImg4UseRectHght < Screen.AllScreens[i].WorkingArea.Height) BackImg4UseRectHght = Screen.AllScreens[i].WorkingArea.Height;
            }

            return new Rectangle(0, 0, BackImg4UseRectWdth, BackImg4UseRectHght);
        }

        private Bitmap ScreenCapture_NET(Rectangle TargetRect)
        {
            using (Graphics graphics4Scr = this.CreateGraphics())
            {
                var curCapt = new Bitmap(TargetRect.Width, TargetRect.Height, graphics4Scr);

                using (var graphics4Bmp = Graphics.FromImage(curCapt))
                {
                    graphics4Bmp.CopyFromScreen(TargetRect.Location, TargetRect.Location, TargetRect.Size);
                    return curCapt;
                }
            }

        }

        private Bitmap ScreenCapture_GDI(Rectangle TargetRect)
        {
            //using (Graphics graphics4Scr = this.CreateGraphics())
            using (Graphics graphics4Scr = Graphics.FromHwnd(IntPtr.Zero))
            {
                var curCapt = new Bitmap(TargetRect.Width, TargetRect.Height, graphics4Scr);

                using (Graphics graphics4Bmp = Graphics.FromImage(curCapt))
                {
                    IntPtr dcScr = graphics4Scr.GetHdc();
                    IntPtr dcBmp = graphics4Bmp.GetHdc();

                    BitBlt(dcBmp, 0, 0, TargetRect.Width, TargetRect.Height, dcScr, 0, 0, TernaryRasterOperations.SRCCOPY);

                    graphics4Scr.ReleaseHdc(dcScr);
                    graphics4Bmp.ReleaseHdc(dcBmp);

                    //MyImage.Save(@"c:\Captured.jpg", ImageFormat.Jpeg);
                    //MessageBox.Show("Finished Saving Image");

                    return curCapt;
                }
            }
        }

        private Bitmap ScreenCapture_GDI2(Rectangle TargetRect)
        {
            IntPtr handle = GetDesktopWindow();

            // get te hDC of the target window
            IntPtr hdcSrc = GetWindowDC(handle);

            // get the size
            var windowRect = new RECT();

            if (TargetRect.IsEmpty)
                GetWindowRect(handle, ref windowRect);
            else
            {
                windowRect.left = TargetRect.Left;
                windowRect.top = TargetRect.Top;
                windowRect.right = TargetRect.Right;
                windowRect.bottom = TargetRect.Bottom;
            }

            int width = windowRect.right - windowRect.left;
            int height = windowRect.bottom - windowRect.top;

            // create a device context we can copy to
            IntPtr hdcDest = CreateCompatibleDC(hdcSrc);
            // create a bitmap we can copy it to, using GetDeviceCaps to get the width/height
            IntPtr hBitmap = CreateCompatibleBitmap(hdcSrc, width, height);
            // select the bitmap object
            IntPtr hOld = SelectObject(hdcDest, hBitmap);
            // bitblt over
            BitBlt(hdcDest, 0, 0, width, height, hdcSrc, 0, 0, TernaryRasterOperations.SRCCOPY);
            // restore selection
            SelectObject(hdcDest, hOld);
            // clean up 
            DeleteDC(hdcDest);
            ReleaseDC(handle, hdcSrc);
            // get a .NET image object for it
            //Image img = Image.FromHbitmap(hBitmap);
            var retval = Bitmap.FromHbitmap(hBitmap);

            // free up the Bitmap object
            DeleteObject(hBitmap);

            return retval;
        }

        #endregion

        #region Methods / Blur
        
        Bitmap BackImg4Use = null;
        Rectangle BackImg4UseRect = Rectangle.Empty;
        bool UseGdiAPI = true;
        
        private void GetBkgImgAndBlurIt(int BlurAmount = 15, bool UseZeroOpacity = true)
        {
            BackImg4UseRect = GetRectangleForCapture();

            var prvTrnspcy = this.AllowTransparency; var prvOpacity = this.Opacity;
            if (UseZeroOpacity) { this.AllowTransparency = true; this.Opacity = 0d; }

            var curBmpSrc = UseGdiAPI ? ScreenCapture_GDI2(BackImg4UseRect) : ScreenCapture_NET(BackImg4UseRect);

            if (UseZeroOpacity) { this.AllowTransparency = prvTrnspcy; this.Opacity = prvOpacity; }

            BackImg4Use = BlurAmount <= 0 ? curBmpSrc : new GaussianBlur(curBmpSrc).Process(BlurAmount: BlurAmount);

            if (!UseGdiAPI && this.RightToLeftLayout && this.RightToLeft == RightToLeft.Yes)
            {
                (BackImg4Use as Image).RotateFlip(RotateFlipType.RotateNoneFlipX);
            }

        }

        #endregion

        #region Methods / Draw / DrawBitmapUsingGDI
        void DrawBitmapUsingGDI(PaintEventArgs e, Bitmap bmp, Rectangle TargetRect)
        {
            IntPtr pTarget = e.Graphics.GetHdc();
            IntPtr pSource = CreateCompatibleDC(pTarget);
            IntPtr pBmp = bmp.GetHbitmap();
            IntPtr pOrig = SelectObject(pSource, pBmp);

            BitBlt(pTarget, 0, 0, TargetRect.Width, TargetRect.Height, pSource, TargetRect.X, TargetRect.Y, TernaryRasterOperations.SRCCOPY);

            Marshal.Release(pOrig);
            DeleteObject(pBmp);
            Marshal.Release(pBmp);
            DeleteDC(pSource);
            e.Graphics.ReleaseHdc(pTarget);
        }
        #endregion


        #region Overrides / OnPaintBackground

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            //if (ModifierKeys.HasFlag(Keys.Control)) panel3.Hide();
            //if (ModifierKeys.HasFlag(Keys.Control)) GetBkgImg();
            //if (ModifierKeys.HasFlag(Keys.Shift)) frmImageView.ShowImageFullScreen(BackImg4Use, this);
            //base.OnPaintBackground(e);

            var curLoc = this.PointToScreen(Point.Empty);
            //var curLoc = this.RightToLeftLayout
            //                ? this.PointToScreen(new Point(this.Width, 0))
            //                : this.PointToScreen(Point.Empty);
            //var curRct = new Rectangle(-curLoc.X, -curLoc.Y, this.Width, this.Height);
            //e.Graphics.DrawImageUnscaledAndClipped(BackImg4Use, new Rectangle(-curLoc.X, -curLoc.Y, BackImg4Use.Width, BackImg4Use.Height));
            //e.Graphics.DrawImageUnscaled(BackImg4Use, curRct);

            if (UseGdiAPI)
            {
                var curRct = this.RightToLeftLayout
                                ? new Rectangle(BackImg4UseRect.Width - curLoc.X, curLoc.Y, this.Width, this.Height)
                                : new Rectangle(curLoc.X, curLoc.Y, this.Width, this.Height);

                DrawBitmapUsingGDI(e, BackImg4Use, curRct);
            }
            else
            {
                var curRct = this.RightToLeftLayout
                                ? new Rectangle(-(BackImg4UseRect.Width - curLoc.X), -curLoc.Y, this.Width, this.Height)
                                : new Rectangle(-curLoc.X, -curLoc.Y, this.Width, this.Height);

                e.Graphics.DrawImageUnscaled(BackImg4Use, curRct);
            }

        }
        
        #endregion


    }
}
