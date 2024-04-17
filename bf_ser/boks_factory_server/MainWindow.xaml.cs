using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics; //TCP
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net; //TCP
using System.Net.Sockets; //TCP
using System.Text;
using System.Threading.Tasks;
using System.Windows;

using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

//OpenCV
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System.Windows.Threading;
using System.Threading;

namespace WPF
{
    /// <summary>
    /// MainWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        TcpListener server = null;
        Dictionary<Stream, string> clntList = new Dictionary<Stream, string>();

        int tCount = 0;
        int count = 0;

        readonly object thisLock = new object();
        bool lockedCount = false;

        // 전역으로 현재 검사중인 색/도형 저장
        string color = null;
        string shape = null;

        public MainWindow()
        {
            InitializeComponent();
            totalCount.Content = 0;

            //서버 코드 작성
            string bindIP = "10.10.20.103";
            const int bindPort = 9090;

            try
            {
                IPEndPoint localAdr = new IPEndPoint(IPAddress.Parse(bindIP), bindPort);//주소 정보 설정

                server = new TcpListener(localAdr); //TCPListener 객체 생성

                server.Start(); //서버 오픈

                Thread t1 = new Thread(new ThreadStart(ClientConnect));
                t1.Start();
            }
            catch (SocketException err) //소켓 오류 날때 예외처리
            {
                MessageBox.Show(err.ToString());
            }
        }


        // 스레드로 다중 클라이언트 연결
        private void ClientConnect()
        {
            while (true)
            {
                TcpClient client = server.AcceptTcpClient();
                NetworkStream stream = client.GetStream();
                count = 0;
                lock (thisLock)
                {
                    while (lockedCount == true)
                        Monitor.Wait(thisLock);
                    lockedCount = true;
                    count++;
                    clntList.Add(stream, count.ToString());
                    lockedCount = false;
                    Monitor.Pulse(thisLock);
                }

                FileThread(client, stream);
            }
        }

        // 메시지 받음
        private async void FileThread(object c, object st)
        {
            int ccCount = 0, ncCount = 0, dcCount = 0;
            string colorShape = null;
            TcpClient client = (TcpClient)c;
            NetworkStream stream = (NetworkStream)st;


            Dictionary<string, int> cc = new Dictionary<string, int>();
            Dictionary<string, int> nc = new Dictionary<string, int>();
            Dictionary<string, int> dc = new Dictionary<string, int>();

            try
            {
                await Task.Run(async () =>
                {
                    while (true)
                    {
                        byte[] sendMsg = new byte[1024];
                        stream.Read(sendMsg, 0, sendMsg.Length);

                        string sendString = Encoding.Default.GetString(sendMsg, 0, sendMsg.Length);
                        string[] splitMsg = sendString.Split('^');

                        switch (splitMsg[0])
                        {
                            case "1":
                                color = splitMsg[1];
                                shape = splitMsg[2];
                                colorShape = color + shape;

                                if (cc.ContainsKey(colorShape))
                                {
                                    ccCount = cc[colorShape];
                                    ncCount = nc[colorShape];
                                    dcCount = dc[colorShape];
                                }
                                else
                                {
                                    ccCount = 0;
                                    ncCount = 0;
                                    dcCount = 0;
                                    cc.Add(colorShape, ccCount);
                                    nc.Add(colorShape, ncCount);
                                    dc.Add(colorShape, dcCount);
                                }

                                bool check = true;
                                this.Dispatcher.Invoke(() =>
                                {
                                    ClientView.ItemsSource = Factory.GetInstance();
                                    choiceColor.Content = color;
                                    choiceShape.Content = shape;

                                    for (int i = 0; i < Factory.GetInstance().Count; i++)
                                    {
                                        Factory data = Factory.GetInstance().ElementAt(i);

                                        if (data.Shape == shape && data.Color == color)
                                        {
                                            check = false;
                                            break;
                                        }
                                    }
                                    if (check)
                                    {
                                        Factory.GetInstance().Add(new Factory() { client = clntList[stream], Color = color, Shape = shape, Normal = 0, Defective = 0, Total = 0 });
                                        ClientView.ItemsSource = Factory.GetInstance();
                                    }
                                });
                                await Task.Delay(100);
                                break;

                            case "2":
                                string colorPrint = null;
                                string shapePrint = null;
                                tCount++;

                                //데이터 크기 수신
                                byte[] size = new byte[4];
                                stream.Read(size, 0, 4);

                                byte[] bytes = new byte[BitConverter.ToInt32(size, 0)];
                                stream.Read(bytes, 0, bytes.Length);

                                List<byte> buf = new List<byte>();
                                buf.AddRange(bytes);

                                // 색과 도형을 검출했음
                                Mat frame = new Mat();
                                frame = Mat.FromImageData(buf.ToArray(), ImreadModes.AnyColor);

                                Mat test_img = frame;
                                Mat gr_line_img = new Mat(); // 흑백으로 바꾸고 외곽선만 남긴 이미지 담을 Mat 초기화
                                Cv2.CvtColor(test_img, gr_line_img, ColorConversionCodes.BGR2GRAY); // BGR이미지 test_img를 GRAY이미지 gr_img로 변환
                                gr_line_img = gr_line_img.Canny(75, 200, 3, true); // 외곽선 추출 함수 | (최소 임계값, 최대 임계값, 소벨 연산 마스크 크기, L2그레디언트)
                                                                                   // 픽셀값이 최소 임계보다 낮으면 가장자리 X, 최대 임계보다 높으면 가장자리 O, 3이 일반적, 정확히 계산할 것인지
                                OpenCvSharp.Point[][] conto_p; // 윤곽선의 점 선언
                                HierarchyIndex[] conto_hierarchy; // 윤곽선의 계층 구조 저장
                                string toxy = null;
                                Cv2.FindContours(gr_line_img, out conto_p, out conto_hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                                for (int i = 0; i < conto_p.Length; i++)
                                {
                                    double length = Cv2.ArcLength(conto_p[i], true); // 윤곽선 전체 길이 함수 | (윤곽 혹은 곡선 담은 포인트, 곡선의 닫힘 정보<true = 시작점-끝점 연결 계산>)

                                    OpenCvSharp.Point[] pp = Cv2.ApproxPolyDP(conto_p[i], length * 0.02, true);
                                    // 윤곽의 근사함수 | (윤곽 혹은 곡선 담은 포인트, 근사치 최대 거리(근사치 정확도), 폐곡선 여부) | 근사치 최대 거리는 보통 전체 윤곽선 길이의 1~5% 값
                                    // 윤곽점 배열 conto_p[i]에서 근사치 최대 거리값으로 폐곡선(시작점-끝점 연결)인 다각형 근사(단순화) 진행
                                    RotatedRect rrect = Cv2.MinAreaRect(pp); // 윤곽선의 경계를 둘러싸는 사각형 계산 / RotatedRect 구조체 반환
                                    Moments moments = Cv2.Moments(conto_p[i], false);

                                    double cx = moments.M10 / moments.M00;
                                    double cy = moments.M01 / moments.M00;
                                    OpenCvSharp.Point pnt = new OpenCvSharp.Point(cx, cy);

                                    int x = Convert.ToInt32(cx);
                                    int y = Convert.ToInt32(cy);

                                    Vec3b color = test_img.At<Vec3b>(x, y);
                                    if (color.Item0 > color.Item1 && color.Item0 > color.Item2)
                                    {
                                        string B = color.Item0.ToString();
                                        string G = color.Item1.ToString();
                                        string R = color.Item2.ToString();
                                        colorPrint = " BLUE";
                                    }
                                    else if (color.Item1 > color.Item0 && color.Item1 > color.Item2)
                                    {
                                        string B = color.Item0.ToString();
                                        string G = color.Item1.ToString();
                                        string R = color.Item2.ToString();
                                        colorPrint = " GREEN";
                                    }
                                    else if (color.Item2 > color.Item0 && color.Item2 > color.Item1)
                                    {
                                        string B = color.Item0.ToString();
                                        string G = color.Item1.ToString();
                                        string R = color.Item2.ToString();
                                        colorPrint = " RED";
                                    }

                                    if (pp.Length == 3)
                                    {
                                        Cv2.DrawContours(test_img, conto_p, i, Scalar.Red, 5, LineTypes.AntiAlias);
                                        // 윤곽선 그리기 함수 | (윤곽선 그릴 이미지, 윤곽 정보 담긴 Mat, 윤곽선 번호(-1일 때, 모든 윤곽선 그림), 색상, 두께, 선형 타입)
                                        shapePrint = " Triangle";
                                    }
                                    else if (pp.Length == 4)
                                    {
                                        Cv2.DrawContours(test_img, conto_p, i, Scalar.Orange, 5, LineTypes.AntiAlias);
                                        shapePrint = " Square";
                                    }
                                    else if (pp.Length == 5)
                                    {
                                        Cv2.DrawContours(test_img, conto_p, i, Scalar.Yellow, 5, LineTypes.AntiAlias);
                                        shapePrint = " Pentagon";
                                    }
                                    else if (pp.Length == 6)
                                    {
                                        Cv2.DrawContours(test_img, conto_p, i, Scalar.Green, 5, LineTypes.AntiAlias);
                                        shapePrint = " Hexagon";
                                    }
                                    else
                                    {
                                        Cv2.DrawContours(test_img, conto_p, i, Scalar.Blue, 5, LineTypes.AntiAlias);
                                        shapePrint = " null";
                                    }
                                    toxy = colorPrint + shapePrint + "(" + x.ToString() + "." + y.ToString() + ")";
                                    Cv2.PutText(test_img, toxy, pnt, HersheyFonts.HersheySimplex, 0.5, Scalar.Black, 2, LineTypes.Link8, false);
                                }

                                // 원래의 색/도형과, 비교할 색/도형 변수
                                if (color == colorPrint && shape == shapePrint)
                                {
                                    ncCount++;
                                    nc[colorShape] = ncCount;
                                }
                                else
                                {
                                    dcCount++;
                                    dc[colorShape] = dcCount;
                                }
                                ccCount++;
                                cc[colorShape] = ccCount;


                                bool rcheck = true;
                                this.Dispatcher.Invoke(() =>
                                {
                                    Cam_1.Source = WriteableBitmapConverter.ToWriteableBitmap(test_img);

                                    totalCount.Content = tCount.ToString();

                                    for (int i = 0; i < Factory.GetInstance().Count; i++)
                                    {
                                        Factory data = Factory.GetInstance().ElementAt(i);

                                        if (data.Shape == shape && data.Color == color)
                                        {
                                            data.Normal = ncCount;
                                            data.Defective = dcCount;
                                            data.Total = ccCount;
                                            ClientView.Items.Refresh();
                                            rcheck = false;
                                            break;
                                        }
                                    }

                                    if (rcheck)
                                    {
                                        ClientView.ItemsSource = Factory.GetInstance();
                                    }
                                });
                                await Task.Delay(100);
                                break;

                            case "3":
                                string sreadMsg = null;

                                for (int i = 0; i < cc.Count; i++)
                                {
                                    // 전체
                                    string ccccc = cc.Keys.ElementAt(i) + "^" + cc.Values.ElementAt(i).ToString();
                                    // 
                                    string ddddc = dc.Values.ElementAt(i).ToString();
                                    // 
                                    string nnnnc = nc.Values.ElementAt(i).ToString();
                                    sreadMsg += ccccc + "^" + ddddc + "^" + nnnnc + "^";
                                }

                                byte[] readMsg = new byte[1024];
                                readMsg = Encoding.Default.GetBytes(sreadMsg);
                                stream.Write(readMsg, 0, readMsg.Length);
                                break;
                        }
                    }
                });
            }
            catch
            {
                MessageBox.Show("클라연결 끊김ㅜ");
            }
            finally
            {
                stream.Close();
                client.Close();
            }
        }



        public class Factory
        {
            public string client { get; set; }
            public string Color { get; set; }
            public string Shape { get; set; }
            public int Normal { get; set; }
            public int Defective { get; set; }
            public int Total { get; set; }

            private static List<Factory> instance;

            public static List<Factory> GetInstance()
            {
                if (instance == null)
                    instance = new List<Factory>();
                return instance;
            }
        }
    }
}