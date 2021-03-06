using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using OSGeo.GDAL;

namespace Khôi_phục_JPEG
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }
        private void button1_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                DialogResult result = fbd.ShowDialog();
                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    textBox1.Text = fbd.SelectedPath;
                    textBox2.Text = fbd.SelectedPath + "\\repaired";
                    string[] filePaths = Directory.GetFiles(fbd.SelectedPath, @"*.*");
                    checkedListBox1.Items.Clear();
                    int i = 0;
                    foreach (string filePath in filePaths)
                    {
                        checkedListBox1.Items.Add(filePath.Replace(textBox1.Text + "\\",""));
                        checkedListBox1.SetItemChecked(i, true);
                        i++;
                    }
                }
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                DialogResult result = fbd.ShowDialog();
                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    textBox2.Text = fbd.SelectedPath;
                }
            }
        }
        private void button3_Click(object sender, EventArgs e)
        {
            tabControl1.SelectTab(OutputTab);
            Directory.CreateDirectory(textBox2.Text);
            foreach (string file in checkedListBox1.CheckedItems)
            {
                byte[] data = File.ReadAllBytes(textBox1.Text + "\\" + file).Skip(153605).ToArray();
                File.WriteAllBytes(textBox2.Text + "\\" + file, data);
                Process p = new Process();
                p.StartInfo.FileName = "JpegRecovery.exe";
                p.StartInfo.Arguments = textBox2.Text + "\\" + file;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardInput = true;
                p.Start();
                p.WaitForExit();
                File.Delete(textBox2.Text + "\\" + file);
                new WhiteBalance().AplyWhiteBalance(textBox2.Text + "\\" + file + ".jpg", textBox2.Text + "\\" + file.Substring(0, file.IndexOf(".")) + ".tiff");
                File.Delete(textBox2.Text + "\\" + file + ".jpg");
                System.Drawing.Bitmap.FromFile(textBox2.Text + "\\" + file.Substring(0, file.IndexOf(".")) + ".tiff").Save(textBox2.Text + "\\" + file.Substring(0, file.IndexOf(".")) + ".jpeg", System.Drawing.Imaging.ImageFormat.Jpeg);
                LogBox.Text = LogBox.Text + file + " - REPAIRED\r\n";
            }
        }
        public class WhiteBalance
        {

            private double percentForBalance = 0.6;

            public WhiteBalance()
            {
                GdalConfiguration.ConfigureGdal();
            }

            public WhiteBalance(double percentForBalance)
            {
                this.percentForBalance = percentForBalance;
                GdalConfiguration.ConfigureGdal();
            }

            public void AplyWhiteBalance(string imagePath, string outImagePath)
            {

                using (Dataset image = Gdal.Open(imagePath, Access.GA_ReadOnly))
                {

                    Band redBand = GetBand(image, ColorInterp.GCI_RedBand);
                    Band greenBand = GetBand(image, ColorInterp.GCI_GreenBand);
                    Band blueBand = GetBand(image, ColorInterp.GCI_BlueBand);

                    if (redBand == null || greenBand == null || blueBand == null)
                    {
                        throw new NullReferenceException("One or more bands are not available.");
                    }

                    int width = redBand.XSize;
                    int height = redBand.YSize;

                    using (Dataset outImage = Gdal.GetDriverByName("GTiff").Create(outImagePath, width, height, 3, DataType.GDT_Byte, null))
                    {

                        double[] geoTransformerData = new double[6];
                        image.GetGeoTransform(geoTransformerData);
                        outImage.SetGeoTransform(geoTransformerData);
                        outImage.SetProjection(image.GetProjection());

                        Band outRedBand = outImage.GetRasterBand(1);
                        Band outGreenBand = outImage.GetRasterBand(2);
                        Band outBlueBand = outImage.GetRasterBand(3);

                        int[] red = new int[width * height];
                        int[] green = new int[width * height];
                        int[] blue = new int[width * height];
                        redBand.ReadRaster(0, 0, width, height, red, width, height, 0, 0);
                        greenBand.ReadRaster(0, 0, width, height, green, width, height, 0, 0);
                        blueBand.ReadRaster(0, 0, width, height, blue, width, height, 0, 0);

                        int[] outRed = WhiteBalanceBand(red);
                        int[] outGreen = WhiteBalanceBand(green);
                        int[] outBlue = WhiteBalanceBand(blue);
                        outRedBand.WriteRaster(0, 0, width, height, outRed, width, height, 0, 0);
                        outGreenBand.WriteRaster(0, 0, width, height, outGreen, width, height, 0, 0);
                        outBlueBand.WriteRaster(0, 0, width, height, outBlue, width, height, 0, 0);

                        outImage.FlushCache();
                    }
                }
            }

            public int[] WhiteBalanceBand(int[] band)
            {
                int[] sortedBand = new int[band.Length];
                Array.Copy(band, sortedBand, band.Length);
                Array.Sort(sortedBand);

                double perc05 = Percentile(sortedBand, percentForBalance);
                double perc95 = Percentile(sortedBand, 100.0 - percentForBalance);

                int[] bandBalanced = new int[band.Length];

                for (int i = 0; i < band.Length; i++)
                {

                    double valueBalanced = (band[i] - perc05) * 255.0 / (perc95 - perc05);
                    bandBalanced[i] = LimitToByte(valueBalanced);
                }

                return bandBalanced;
            }

            public double Percentile(int[] sequence, double percentile)
            {

                int nSequence = sequence.Length;
                double nPercent = (nSequence + 1) * percentile / 100d;
                if (nPercent == 1d)
                {
                    return sequence[0];
                }
                else if (nPercent == nSequence)
                {
                    return sequence[nSequence - 1];
                }
                else
                {
                    int intNPercent = (int)nPercent;
                    double d = nPercent - intNPercent;
                    return sequence[intNPercent - 1] + d * (sequence[intNPercent] - sequence[intNPercent - 1]);
                }
            }

            private byte LimitToByte(double value)
            {
                byte newValue;

                if (value < 0)
                {
                    newValue = 0;
                }
                else if (value > 255)
                {
                    newValue = 255;
                }
                else
                {
                    newValue = (byte)value;
                }

                return newValue;
            }

            /**
              * Returns the band for an color (red, green, blue or alpha)
              * The dataset should have 4 bands
              * */
            public static Band GetBand(Dataset ImageDataSet, ColorInterp colorInterp)
            {
                if (colorInterp.Equals(ImageDataSet.GetRasterBand(1).GetRasterColorInterpretation()))
                {
                    return ImageDataSet.GetRasterBand(1);
                }
                else if (colorInterp.Equals(ImageDataSet.GetRasterBand(2).GetRasterColorInterpretation()))
                {
                    return ImageDataSet.GetRasterBand(2);
                }
                else if (colorInterp.Equals(ImageDataSet.GetRasterBand(3).GetRasterColorInterpretation()))
                {
                    return ImageDataSet.GetRasterBand(3);
                }
                else
                {
                    return ImageDataSet.GetRasterBand(4);
                }
            }

        }
    }
}
