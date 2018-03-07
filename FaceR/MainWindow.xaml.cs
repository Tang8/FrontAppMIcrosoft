using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Accord.Video.FFMPEG;
using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;
using Microsoft.Win32;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;

namespace FaceR
{
    public partial class MainWindow
    {
        // Replace the first parameter with your valid subscription key.
        //
        // Replace or verify the region in the second parameter.
        //
        // You must use the same region in your REST API call as you used to obtain your subscription keys.
        // For example, if you obtained your subscription keys from the westus region, replace
        // "westcentralus" in the URI below with "westus".
        //
        // NOTE: Free trial subscription keys are generated in the westcentralus region, so if you are using
        // a free trial subscription key, you should not need to change this region.
        private readonly IFaceServiceClient _faceServiceClient =
            new FaceServiceClient("3d7d23e210144e1ab01e5f7a335d0a1d", "https://westcentralus.api.cognitive.microsoft.com/face/v1.0");

        private Face[] _faces;                   // The list of detected faces.
        private string[] _faceDescriptions;      // The list of descriptions for the detected faces.
        private double _resizeFactor;            // The resize factor for the displayed image.

        public MainWindow()
        {
            InitializeComponent();
        }

        // Displays the image and calls Detect Faces.

        private async void BrowseButton_Click_Picture(object sender, RoutedEventArgs e)
        {
            // Get the image file to scan from the user.
            var openDlg = new OpenFileDialog
            {
                Filter = "Image files(*.jpg, *.jpeg, *.jpe, *.jfif, *.png) | *.jpg; *.jpeg; *.jpe; *.jfif; *.png"
            };

            var result = openDlg.ShowDialog(this);

            // Return if canceled.
            if (!(bool)result)
            {
                return;
            }

            // Process image file.
            var filePath = openDlg.FileName;

            var fileUri = new Uri(filePath);
            var bitmapSource = new BitmapImage();

            bitmapSource.BeginInit();
            bitmapSource.CacheOption = BitmapCacheOption.None;
            bitmapSource.UriSource = fileUri;
            bitmapSource.EndInit();

            _faces = await UploadAndDetectFaces(filePath);
            FacePhoto.Source = bitmapSource;

            if (_faces.Length <= 0) return;
            // Prepare to draw rectangles around the faces.
            var visual = new DrawingVisual();
            var drawingContext = visual.RenderOpen();
            drawingContext.DrawImage(bitmapSource,
                new Rect(0, 0, bitmapSource.Width, bitmapSource.Height));
            var dpi = bitmapSource.DpiX;
            _resizeFactor = 96 / dpi;
            _faceDescriptions = new string[_faces.Length];

            for (var i = 0; i < _faces.Length; ++i)
            {
                var face = _faces[i];

                // Draw a rectangle on the face.
                drawingContext.DrawRectangle(
                    Brushes.Transparent,
                    new Pen(Brushes.Red, 2),
                    new Rect(
                        face.FaceRectangle.Left * _resizeFactor,
                        face.FaceRectangle.Top * _resizeFactor,
                        face.FaceRectangle.Width * _resizeFactor,
                        face.FaceRectangle.Height * _resizeFactor
                    )
                );

                // Store the face description.
                _faceDescriptions[i] = FaceDescription(face);
            }

            drawingContext.Close();

            // Display the image with the rectangle around the face.
            var faceWithRectBitmap = new RenderTargetBitmap(
                (int)(bitmapSource.PixelWidth * _resizeFactor),
                (int)(bitmapSource.PixelHeight * _resizeFactor),
                96,
                96,
                PixelFormats.Pbgra32);

            faceWithRectBitmap.Render(visual);
            FacePhoto.Source = faceWithRectBitmap;
        }

        private void BrowseButton_Click_Video(object sender, RoutedEventArgs e)
        {
            // Get the image file to scan from the user.
            var openDlg = new OpenFileDialog();
            
            const string formats = "All Videos Files |*.dat; *.wmv; *.3g2; *.3gp; *.3gp2; *.3gpp; *.amv; *.asf;  *.avi; *.bin; *.cue; *.divx; *.dv; *.flv; *.gxf; *.iso; *.m1v; *.m2v; *.m2t; *.m2ts; *.m4v; " +
                                   " *.mkv; *.mov; *.mp2; *.mp2v; *.mp4; *.mp4v; *.mpa; *.mpe; *.mpeg; *.mpeg1; *.mpeg2; *.mpeg4; *.mpg; *.mpv2; *.mts; *.nsv; *.nuv; *.ogg; *.ogm; *.ogv; *.ogx; *.ps; *.rec; *.rm; *.rmvb; *.tod; *.ts; *.tts; *.vob; *.vro; *.webm";

            openDlg.Filter = formats;
            var result = openDlg.ShowDialog(this);

            // Return if canceled.
            if (!(bool)result)
            {
                return;
            }
            var filePath = openDlg.FileName;

            // Draw prepare
            var reader = new VideoFileReader();
            reader.Open(filePath);
            Mess.Text = "width:  " + reader.Width + "\n";
            Mess.Text = Mess.Text + "height: " + reader.Height + "\n";
            Mess.Text = Mess.Text + "fps:    " + reader.FrameRate + "\n";
            Mess.Text = Mess.Text + "codec:  " + reader.CodecName + "\n";

            // read 100 video frames out of it
            for (var i = 0; i < 100; i++)
            {
                var bitmapSource = new Bitmap(reader.ReadVideoFrame());
                FacePhoto.Source = Convert(bitmapSource);
            }
            reader.Close();
            Mess.Text = Mess.Text + filePath + "\n";
        }

        private static BitmapImage Convert(Image src)
        {
            var ms = new MemoryStream();
            src.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
            var image = new BitmapImage();
            image.BeginInit();
            ms.Seek(0, SeekOrigin.Begin);
            image.StreamSource = ms;
            image.EndInit();
            return image;
        }

        // Displays the face description when the mouse is over a face rectangle.

        private void FacePhoto_MouseMove(object sender, MouseEventArgs e)
        {
            // If the REST call has not completed, return from this method.
            if (_faces == null)
                return;

            // Find the mouse position relative to the image.
            var mouseXy = e.GetPosition(FacePhoto);

            var imageSource = FacePhoto.Source;
            var bitmapSource = (BitmapSource)imageSource;

            // Scale adjustment between the actual size and displayed size.
            var scale = FacePhoto.ActualWidth / (bitmapSource.PixelWidth / _resizeFactor);

            // Check if this mouse position is over a face rectangle.

            for (var i = 0; i < _faces.Length; ++i)
            {
                var fr = _faces[i].FaceRectangle;
                var left = fr.Left * scale;
                var top = fr.Top * scale;
                var width = fr.Width * scale;
                var height = fr.Height * scale;

                // Display the face description for this face if the mouse is over this face rectangle.
                if (mouseXy.X >= left && mouseXy.X <= left + width && mouseXy.Y >= top && mouseXy.Y <= top + height)
                {
                    FaceDescriptionStatusBar.Text = _faceDescriptions[i];
                    break;
                } else if (_faces.Length == 1) {
                    FaceDescriptionStatusBar.Text = _faceDescriptions[0];
                    break;
                }
            }
        }

        // Uploads the image file and calls Detect Faces.

        private async Task<Face[]> UploadAndDetectFaces(string imageFilePath)
        {
            // The list of Face attributes to return.
            IEnumerable<FaceAttributeType> faceAttributes =
                new[] { FaceAttributeType.Gender, FaceAttributeType.Age, FaceAttributeType.Smile, FaceAttributeType.Emotion, FaceAttributeType.Glasses, FaceAttributeType.Hair };

            // Call the Face API.
            try
            {
                using (Stream imageFileStream = File.OpenRead(imageFilePath))
                {
                    var faces = await _faceServiceClient.DetectAsync(imageFileStream, returnFaceId: true, returnFaceLandmarks: false, returnFaceAttributes: faceAttributes);
                    return faces;
                }
            }
            // Catch and display Face API errors.
            catch (FaceAPIException f)
            {
                MessageBox.Show(f.ErrorMessage, f.ErrorCode);
                return new Face[0];
            }
            // Catch and display all other errors.
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Error");
                return new Face[0];
            }
        }

        // Returns a string that describes the given face.

        private static string FaceDescription(Face face)
        {
            var sb = new StringBuilder();

            sb.Append("Face: ");

            // Add the gender, age, and smile.
            sb.Append(face.FaceAttributes.Gender);
            sb.Append(", ");
            sb.Append(face.FaceAttributes.Age);
            sb.Append(", ");
            sb.Append($"smile {face.FaceAttributes.Smile * 100:F1}%, ");

            // Add the emotions. Display all emotions over 10%.
            sb.Append("Emotion: ");
            var emotionScores = face.FaceAttributes.Emotion;
            if (emotionScores.Anger >= 0.1f) sb.Append($"anger {emotionScores.Anger * 100:F1}%, ");
            if (emotionScores.Contempt >= 0.1f) sb.Append($"contempt {emotionScores.Contempt * 100:F1}%, ");
            if (emotionScores.Disgust >= 0.1f) sb.Append($"disgust {emotionScores.Disgust * 100:F1}%, ");
            if (emotionScores.Fear >= 0.1f) sb.Append($"fear {emotionScores.Fear * 100:F1}%, ");
            if (emotionScores.Happiness >= 0.1f) sb.Append($"happiness {emotionScores.Happiness * 100:F1}%, ");
            if (emotionScores.Neutral >= 0.1f) sb.Append($"neutral {emotionScores.Neutral * 100:F1}%, ");
            if (emotionScores.Sadness >= 0.1f) sb.Append($"sadness {emotionScores.Sadness * 100:F1}%, ");
            if (emotionScores.Surprise >= 0.1f) sb.Append($"surprise {emotionScores.Surprise * 100:F1}%, ");

            // Add glasses.
            sb.Append(face.FaceAttributes.Glasses);
            sb.Append(", ");

            // Add hair.
            sb.Append("Hair: ");

            // Display baldness confidence if over 1%.
            if (face.FaceAttributes.Hair.Bald >= 0.01f)
                sb.Append($"bald {face.FaceAttributes.Hair.Bald * 100:F1}% ");

            // Display all hair color attributes over 10%.
            var hairColors = face.FaceAttributes.Hair.HairColor;
            foreach (var hairColor in hairColors)
            {
                if (!(hairColor.Confidence >= 0.1f)) continue;
                sb.Append(hairColor.Color.ToString());
                sb.Append($" {hairColor.Confidence * 100:F1}% ");
            }

            // Return the built string.
            return sb.ToString();
        }
    }
}