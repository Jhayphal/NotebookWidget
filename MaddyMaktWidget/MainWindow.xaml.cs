using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Tesseract;

namespace MaddyMaktWidget
{
	/// <summary>
	/// Логика взаимодействия для MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		[DllImport("user32.dll", SetLastError = true)]
		static extern int GetWindowLong(IntPtr hWnd, int nIndex);

		[DllImport("user32.dll")]
		static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

		private const int GWL_EX_STYLE = -20;
		private const int WS_EX_APPWINDOW = 0x00040000, WS_EX_TOOLWINDOW = 0x00000080;

        const string storageFileName = "data.bin";
        const string withoutHeader = "* без заголовка *";

        readonly string storageDataFormat = DataFormats.XamlPackage;

		public MainWindow()
		{
			InitializeComponent();
		}

		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			var helper = new WindowInteropHelper(this).Handle;

			SetWindowLong(helper, GWL_EX_STYLE, (GetWindowLong(helper, GWL_EX_STYLE) | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);

            loadChanges();
		}

		private void Expander_PreviewKeyUp(object sender, KeyEventArgs e)
		{
			if (!(sender is Expander exp))
				return;

			if (!(e.OriginalSource is RichTextBox txt))
				return;

			string text = new TextRange(txt.Document.ContentStart, txt.Document.ContentEnd).Text;

			exp.Header = getExpanderHeader(text);

            saveChanges();

            e.Handled = true;
		}

		private void TextBlock_MouseUp(object sender, MouseButtonEventArgs e)
		{
			createNewNote();

            saveChanges();
		}

        async void TextBlock_Drop(object sender, DragEventArgs e)
        {
            var previouslyCursor = this.createNote.Cursor;
            this.createNote.Cursor = Cursors.Wait;

            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    if (!(e.Data.GetData(DataFormats.FileDrop) is string[] bitmaps) || bitmaps.Length != 1)
                        return;

                    var text = await textRecognizeAsync(bitmaps[0]);

                    createNewNoteWithContent(text);
                }
                else if (e.Data.GetDataPresent(DataFormats.Bitmap))
                {
                    var image = e.Data.GetData(DataFormats.Bitmap) as BitmapSource;

                    var bitmap = BitmapFromSource(image);

                    var text = await textRecognizeAsync(bitmap);

                    createNewNoteWithContent(text);
                }

                this.createNote.Cursor = previouslyCursor;
            }
            catch (Exception ex)
            {
                this.createNote.Cursor = previouslyCursor;

                MessageBox.Show(this, ex.Message, this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }

            saveChanges();
        }

        Bitmap BitmapFromSource(BitmapSource bitmapsource)
        {
            Bitmap result;

            using (MemoryStream outStream = new MemoryStream())
            {
                BitmapEncoder enc = new BmpBitmapEncoder();

                enc.Frames.Add(BitmapFrame.Create(bitmapsource));
                enc.Save(outStream);

                var bitmap = new Bitmap(outStream);

                result = ToFormat24bppRgbFormat(bitmap);
            }

            return result;
        }

        Bitmap ToFormat24bppRgbFormat(Bitmap bitmap)
        {
            var result = new Bitmap(bitmap.Width, bitmap.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.DrawImage(bitmap, new System.Drawing.Point(0, 0));
            }

            return result;
        }

        private void TextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			this.DragMove();
		}

		void createNewNote()
		{
			var exp = new Expander
			{
				Header = withoutHeader,
				IsExpanded = true,
				Content = new RichTextBox()
			};

			exp.PreviewKeyUp += Expander_PreviewKeyUp;

			this.mainPanel.Children.Add(exp);
		}

        void createNewNoteWithContent(string content)
        {
            var exp = new Expander
            {
                Header = getExpanderHeader(content),
                IsExpanded = true
            };

            var txt = new RichTextBox();
            txt.Document.Blocks.Add(new Paragraph(new Run(content)));

            exp.Content = txt;

            exp.PreviewKeyUp += Expander_PreviewKeyUp;

            this.mainPanel.Children.Add(exp);
        }

        string getExpanderHeader(string text)
        {
            text = text
                .Replace("\r", string.Empty)
                .Split('\n')[0];

            string result;

            if (string.IsNullOrWhiteSpace(text))
                result = withoutHeader;
            else
                result = text;

            return result;
        }

        async Task<string> textRecognizeAsync(string fileName)
        {
            return await Task.Run(() => textRecognize(fileName));
        }

        async Task<string> textRecognizeAsync(Bitmap bitmap)
        {
            return await Task.Run(() => textRecognize(bitmap));
        }

        string textRecognize(string fileName)
        {
            Bitmap image = new Bitmap(fileName);
            image = ToFormat24bppRgbFormat(image);
            return textRecognize(image);
        }

        string textRecognize(Bitmap image)
        {
            if (true)
                return textRecognizeEx(image);

            string result = string.Empty;

            using (var engine = new TesseractEngine(@"./tessdata", "rus", EngineMode.Default))
            {
                using (var img = PixConverter.ToPix(image))
                {
                    using (var page = engine.Process(img))
                    {
                        var textRegions = page.GetSegmentedRegions(PageIteratorLevel.Block);

                        var padding = 5;

                        var minimumLeft = Math.Max(textRegions.Min(x => x.Left) - padding, 0);
                        var minimumTop = Math.Max(textRegions.Min(x => x.Top) - padding, 0);
                        var maximumRight = Math.Min(textRegions.Max(x => x.Right) + padding, image.Width);
                        var maximumBottom = Math.Min(textRegions.Max(x => x.Bottom) + padding, image.Height);

                        var width = maximumRight - minimumLeft;
                        var height = maximumBottom - minimumTop;

                        var processedImage = new Bitmap(width, height);
                        processedImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

                        var threshold = 128;
                        var maximumDifference = 25;

                        for (int x = minimumLeft; x < maximumRight; ++x)
                        {
                            for (int y = minimumTop; y < maximumBottom; ++y)
                            {
                                var currentPixel = image.GetPixel(x, y);

                                var r = currentPixel.R;
                                var g = currentPixel.G;
                                var b = currentPixel.B;

                                if (Math.Abs(r - g) < maximumDifference && Math.Abs(r - b) < maximumDifference)
                                {
                                    if ((r + g + b) / 3 < threshold)
                                        processedImage.SetPixel(x - minimumLeft, y - minimumTop, System.Drawing.Color.Black);
                                    else
                                        processedImage.SetPixel(x - minimumLeft, y - minimumTop, System.Drawing.Color.White);
                                }
                                else
                                    processedImage.SetPixel(x - minimumLeft, y - minimumTop, System.Drawing.Color.White);
                            }
                        }

                        var scaleFactor = 1.5;
                        var destinationRectangle = new System.Drawing.Rectangle(0, 0, (int)(width * scaleFactor), (int)(height * scaleFactor));

                        image = new Bitmap(destinationRectangle.Width, destinationRectangle.Height);
                        image.SetResolution(processedImage.HorizontalResolution, processedImage.VerticalResolution);

                        using (var graphics = Graphics.FromImage(image))
                        {
                            graphics.CompositingMode = CompositingMode.SourceCopy;
                            graphics.CompositingQuality = CompositingQuality.HighQuality;
                            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            graphics.SmoothingMode = SmoothingMode.HighQuality;
                            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                            using (var wrapMode = new ImageAttributes())
                            {
                                wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                                graphics.DrawImage(processedImage, destinationRectangle, 0, 0, processedImage.Width, processedImage.Height, GraphicsUnit.Pixel, wrapMode);
                            }
                        }
                    }
                }

                using (var img = PixConverter.ToPix(image))
                using (var page = engine.Process(img))
                {
                    result = page.GetText();
                }
            }

            return result;
        }

        string textRecognizeEx(Bitmap image)
        {
            string result = string.Empty;

            using (var engine = new TesseractEngine(@"./tessdata", "rus", EngineMode.Default))
            using (var page = engine.Process(image))
                result = page.GetText();

            return result;
        }

        void loadChanges()
        {
            this.mainPanel.Children.Clear();

            try
            {
                using (var resultStream = new FileStream(storageFileName, FileMode.Open, FileAccess.Read))
                {
                    var bytes = new byte[4];

                    resultStream.Read(bytes, 0, 4);
                    var itemsCount = BitConverter.ToInt32(bytes, 0);

                    var itemsLength = new List<long>(itemsCount);

                    bytes = new byte[8];
                    var index = 0;

                    while (index++ != itemsCount)
                    {
                        resultStream.Read(bytes, 0, 8);
                        var length = BitConverter.ToInt64(bytes, 0);

                        itemsLength.Add(length);
                    }

                    foreach (var length in itemsLength)
                    {
                        bytes = new byte[length];

                        resultStream.Read(bytes, 0, (int)length);

                        var stream = new MemoryStream(bytes);

                        var exp = new Expander
                        {
                            IsExpanded = false
                        };

                        var txt = new RichTextBox();
                        var range = new TextRange(txt.Document.ContentStart, txt.Document.ContentEnd);

                        range.Load(stream, storageDataFormat);

                        exp.Content = txt;
                        exp.Header = getExpanderHeader(range.Text);

                        this.mainPanel.Children.Add(exp);

                        stream.Dispose();
                    }
                }
            }
            catch (FileNotFoundException)
            {
                createNewNote();
            }
            catch (Exception e)
            {
                createNewNote();

                MessageBox.Show(this, e.Message, this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void saveChanges()
        {
            try
            {
                var itemsCount = this.mainPanel.Children.Count;

                using (var resultStream = new FileStream(storageFileName, FileMode.Create, FileAccess.Write))
                {
                    var bytes = BitConverter.GetBytes(itemsCount);
                    resultStream.Write(bytes, 0, bytes.Length);

                    var dataStreams = new List<MemoryStream>(itemsCount);

                    foreach (Expander child in this.mainPanel.Children)
                    {
                        var txt = child.Content as RichTextBox;
                        var range = new TextRange(txt.Document.ContentStart, txt.Document.ContentEnd);
                        var stream = new MemoryStream();

                        range.Save(stream, storageDataFormat);

                        bytes = BitConverter.GetBytes(stream.Length);
                        resultStream.Write(bytes, 0, bytes.Length);

                        dataStreams.Add(stream);
                    }

                    foreach (var stream in dataStreams)
                    {
                        bytes = stream.ToArray();

                        resultStream.Write(bytes, 0, bytes.Length);

                        stream.Dispose();
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(this, e.Message, this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
