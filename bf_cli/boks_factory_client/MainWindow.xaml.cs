using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Threading;
using System.Net.Sockets;
using OpenCvSharp;
using System.Windows.Threading;
namespace nicetry
{
    public partial class MainWindow : System.Windows.Window
    {
        VideoCapture cam;
        Mat frame;
        DispatcherTimer timer;
        bool is_initCam, is_initTimer;
        TcpClient client; //TCP 통신에 사용할 객체
        NetworkStream stream; //데이터 송수신에 사용할 객체
        bool check = true;
        private void connect(object sender, RoutedEventArgs e)
        {

            string colorsel = color.Items.GetItemAt(color.SelectedIndex).ToString().Split(':')[1] + "^";
            string shapesel = shape.Items.GetItemAt(shape.SelectedIndex).ToString().Split(':')[1] + "^";
            string str = "1^" + colorsel + shapesel;
            byte[] bytes1 = new byte[1024];
            bytes1 = Encoding.Default.GetBytes(str);
            stream.Write(bytes1, 0, bytes1.Length);
            color.IsEnabled = false;
            shape.IsEnabled = false;
            btn.IsEnabled = false;
            check = true;
            sendfile();
        }

        private void Btn1_Click(object sender, RoutedEventArgs e)
        {
            check = false;
            color.IsEnabled = true;
            shape.IsEnabled = true;
            btn.IsEnabled = true;
        }

        private async void sendfile()
        {
            await Task.Run(async () =>
            {
                while (check)
                {
                    Thread.Sleep(1000);
                    byte[] bytes1 = new byte[1024];
                    bytes1 = Encoding.Default.GetBytes("2^");
                    stream.Write(bytes1, 0, bytes1.Length);
                    Mat aa = new Mat();
                    cam.Read(aa);
                    byte[] size = new byte[4];
                    size = BitConverter.GetBytes(aa.ToBytes().Length);
                    stream.Write(size, 0, 4); //데이터 크기 먼저 보내기
                    Thread.Sleep(100);
                    byte[] bytes = new byte[aa.ToBytes().Length]; //바이트 배열 생성
                    bytes = aa.ToBytes();
                    stream.Write(bytes, 0, bytes.Length);
                }
            });
        }
        public MainWindow()
        {
            InitializeComponent();
            Allresult.Visibility = Visibility.Hidden;
            sethide.Visibility = Visibility.Hidden;
            CamStart();
            try
            {
                client = new TcpClient(); //클라이언트 객체 생성
                client.Connect("10.10.20.103", 9090); //서버에 연결
                stream = client.GetStream(); //데이터 송수신에 사용할 스트림 생성
            }
            catch (SocketException ea)
            {
                MessageBox.Show(ea.ToString());
            }
        }
        private void CamStart()
        {
            // 카메라, 타이머(0.01ms 간격) 초기화
            is_initCam = init_camera();
            is_initTimer = init_Timer(0.01);
            // 초기화 완료면 타이머 실행
            if (is_initTimer && is_initCam) timer.Start();
        }
        private bool init_Timer(double interval_ms)
        {
            try
            {
                timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(interval_ms);
                timer.Tick += new EventHandler(timer_tick);
                return true;
            }
            catch
            {
                return false;
            }
        }
        private bool init_camera()
        {
            try
            {
                // 0번 카메라로 VideoCapture 생성 (카메라가 없으면 안됨)
                cam = new VideoCapture(0);
                cam.FrameHeight = (int)Cam_1.Height;
                cam.FrameWidth = (int)Cam_1.Width;
                // 카메라 영상을 담을 Mat 변수 생성
                frame = new Mat();
                return true;
            }
            catch
            {
                return false;
            }
        }
        private void Resultbtn_Click(object sender, RoutedEventArgs e)
        {
            Factory.GetInstance().Clear();
            byte[] bytes2 = new byte[1024];
            bytes2 = Encoding.Default.GetBytes("3^");
            stream.Write(bytes2, 0, bytes2.Length);
            byte[] readresult = new byte[1024];
            stream.Read(readresult, 0, readresult.Length);
            string result = Encoding.Default.GetString(readresult, 0, readresult.Length);
            string[] resulttolistview = result.Split('^');
            for (int i = 0; i < resulttolistview.Length - 1; i += 4)
            {


                Factory.GetInstance().Add(new Factory() { Type = resulttolistview[i], Total = resulttolistview[i + 1], Defective = resulttolistview[i + 2], Normal = resulttolistview[i + 3] });
            }
            this.Dispatcher.Invoke(() =>
            {

                Allresult.ItemsSource = Factory.GetInstance();
            });

            Allresult.Visibility = Visibility.Visible;
            sethide.Visibility = Visibility.Visible;
        }
        public class Factory
        {
            public string Type { get; set; }
            public string Total { get; set; }
            public string Defective { get; set; }
            public string Normal { get; set; }
            private static List<Factory> instance;
            public static List<Factory> GetInstance()
            {
                if (instance == null)
                    instance = new List<Factory>();
                return instance;
            }
        }
        private void Sethide_Click(object sender, RoutedEventArgs e)
        {
            Allresult.Visibility = Visibility.Hidden;
            sethide.Visibility = Visibility.Hidden;
            Allresult.ItemsSource = null;

        }
        private void timer_tick(object sender, EventArgs e)
        {
            cam.Read(frame);
            Cam_1.Source = OpenCvSharp.WpfExtensions.WriteableBitmapConverter.ToWriteableBitmap(frame);
        }
    }
}