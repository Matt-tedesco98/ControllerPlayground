using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage.Streams;
using QRCoder;
using System.Threading.Tasks;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ControllerPlayground.Overlays {
    public sealed partial class SteamQrLoginOverlay : UserControl {
        public SteamQrLoginOverlay() {
            InitializeComponent();
        }
        public async Task SetChallangeUrlAsync(string challangeUrl) {
            using QRCodeGenerator generator = new();

            using QRCodeData qeData = generator.CreateQrCode(challangeUrl, QRCodeGenerator.ECCLevel.Q);

            using PngByteQRCode qrCode = new(qeData);

            byte[] pngBytes = qrCode.GetGraphic(10);

            using InMemoryRandomAccessStream stream = new();

            using DataWriter writer = new(stream);

            writer.WriteBytes(pngBytes);

            await writer.StoreAsync();
            await writer.FlushAsync();

            stream.Seek(0);

            BitmapImage bitmapImage = new();

            await bitmapImage.SetSourceAsync(stream);

            QrImage.Source = bitmapImage;
            StatusText.Text = "Scan the QR code with your Steam mobile app to log in.";
        }
    }

}
