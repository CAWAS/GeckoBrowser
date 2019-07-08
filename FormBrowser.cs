﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceModel;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using BrowserLib;
using Gecko;
using Gecko.DOM;
using Gecko.Events;

namespace GeckoBrowser
{
	/// <summary>
	/// ブラウザを表示するフォームです。
	/// </summary>
	/// <remarks>thx KanColleViewer!</remarks>
	[ServiceBehavior(InstanceContextMode = InstanceContextMode.Single/*, IncludeExceptionDetailInFaults = true*/)]
	public partial class FormBrowser : Form, BrowserLib.IBrowser
	{

		private readonly Size KanColleSize = new Size(1200, 720);
		private readonly string BrowserCachePath = "BrowserCache";

		private readonly string StyleClassID = Guid.NewGuid().ToString().Substring(0, 8);
		private bool RestoreStyleSheet = false;

		// FormBrowserHostの通信サーバ
		private string ServerUri;

		// FormBrowserの通信サーバ
		private PipeCommunicator<BrowserLib.IBrowserHost> BrowserHost;

		private BrowserLib.BrowserConfiguration Configuration;

		// 親プロセスが生きているか定期的に確認するためのタイマー
		private Timer HeartbeatTimer = new Timer();
		private IntPtr HostWindow;

		private GeckoWebBrowser Browser = null;

		//private string ProxySettings = null;
		private string HttpProxyHost = null;
		private int HttpProxyPort = 0;
		private string SslProxyHost = null;
		private int SslProxyPort = 0;


		private bool _styleSheetApplied;
		/// <summary>
		/// スタイルシートの変更が適用されているか
		/// </summary>
		private bool StyleSheetApplied
		{
			get { return _styleSheetApplied; }
			set
			{
				if (value)
				{
					//Browser.Anchor = AnchorStyles.None;
					ApplyZoom();
					SizeAdjuster_SizeChanged(null, new EventArgs());
				}
				else
				{
					SizeAdjuster.SuspendLayout();
					if (IsBrowserInitialized)
					{
						//Browser.Anchor = AnchorStyles.Top | AnchorStyles.Left;
						Browser.Location = new Point(0, 0);
						Browser.MinimumSize = new Size(0, 0);
						Browser.Size = SizeAdjuster.Size;
					}
					SizeAdjuster.ResumeLayout();
				}

				_styleSheetApplied = value;
			}
		}

		/// <summary>
		/// 艦これが読み込まれているかどうか
		/// </summary>
		private bool IsKanColleLoaded { get; set; }

		private VolumeManager _volumeManager;

		private string _lastScreenShotPath;


		private NumericUpDown ToolMenu_Other_Volume_VolumeControl
		{
			get { return (NumericUpDown)((ToolStripControlHost)ToolMenu_Other_Volume.DropDownItems["ToolMenu_Other_Volume_VolumeControlHost"]).Control; }
		}

		private PictureBox ToolMenu_Other_LastScreenShot_Control
		{
			get { return (PictureBox)((ToolStripControlHost)ToolMenu_Other_LastScreenShot.DropDownItems["ToolMenu_Other_LastScreenShot_ImageHost"]).Control; }
		}



		/// <summary>
		/// </summary>
		/// <param name="serverUri">ホストプロセスとの通信用URL</param>
		public FormBrowser(string serverUri)
		{
			InitializeComponent();

			ServerUri = serverUri;
			StyleSheetApplied = false;
			_volumeManager = new VolumeManager((uint)Process.GetCurrentProcess().Id);


			// 音量設定用コントロールの追加
			{
				var control = new NumericUpDown();
				control.Name = "ToolMenu_Other_Volume_VolumeControl";
				control.Maximum = 100;
				control.TextAlign = HorizontalAlignment.Right;
				control.Font = ToolMenu_Other_Volume.Font;

				control.ValueChanged += ToolMenu_Other_Volume_ValueChanged;
				control.Tag = false;

				var host = new ToolStripControlHost(control, "ToolMenu_Other_Volume_VolumeControlHost");

				control.Size = new Size(host.Width - control.Margin.Horizontal, host.Height - control.Margin.Vertical);
				control.Location = new Point(control.Margin.Left, control.Margin.Top);


				ToolMenu_Other_Volume.DropDownItems.Add(host);
			}

			// スクリーンショットプレビューコントロールの追加
			{
				double zoomrate = 0.5;
				var control = new PictureBox();
				control.Name = "ToolMenu_Other_LastScreenShot_Image";
				control.SizeMode = PictureBoxSizeMode.Zoom;
				control.Size = new Size((int)(KanColleSize.Width * zoomrate), (int)(KanColleSize.Height * zoomrate));
				control.Margin = new Padding();
				control.Image = new Bitmap((int)(KanColleSize.Width * zoomrate), (int)(KanColleSize.Height * zoomrate), PixelFormat.Format24bppRgb);
				using (var g = Graphics.FromImage(control.Image))
				{
					g.Clear(SystemColors.Control);
					g.DrawString("スクリーンショットをまだ撮影していません。\r\n", Font, Brushes.Black, new Point(4, 4));
				}

				var host = new ToolStripControlHost(control, "ToolMenu_Other_LastScreenShot_ImageHost");

				host.Size = new Size(control.Width + control.Margin.Horizontal, control.Height + control.Margin.Vertical);
				host.AutoSize = false;
				control.Location = new Point(control.Margin.Left, control.Margin.Top);

				host.Click += ToolMenu_Other_LastScreenShot_ImageHost_Click;

				ToolMenu_Other_LastScreenShot.DropDownItems.Insert(0, host);
			}

		}


		private void FormBrowser_Load(object sender, EventArgs e)
		{
			SetWindowLong(this.Handle, GWL_STYLE, WS_CHILD);

			// ホストプロセスに接続
			BrowserHost = new PipeCommunicator<BrowserLib.IBrowserHost>(
				this, typeof(BrowserLib.IBrowser), ServerUri + "Browser", "Browser");
			BrowserHost.Connect(ServerUri + "/BrowserHost");
			BrowserHost.Faulted += BrowserHostChannel_Faulted;


			ConfigurationChanged(BrowserHost.Proxy.Configuration);


			// ウィンドウの親子設定＆ホストプロセスから接続してもらう
			BrowserHost.Proxy.ConnectToBrowser(this.Handle);

			// 親ウィンドウが生きているか確認 
			HeartbeatTimer.Tick += (EventHandler)((sender2, e2) =>
			{
				BrowserHost.AsyncRemoteRun(() => { HostWindow = BrowserHost.Proxy.HWND; });
			});
			HeartbeatTimer.Interval = 2000; // 2秒ごと　
			HeartbeatTimer.Start();


			BrowserHost.AsyncRemoteRun(() => BrowserHost.Proxy.GetIconResource());


			InitializeBrowser();
		}


		/// <summary>
		/// ブラウザを初期化します。
		/// 最初の呼び出しのみ有効です。二回目以降は何もしません。
		/// </summary>
		void InitializeBrowser()
		{
			if (Browser != null)
				return;

			if (HttpProxyPort == 0)
				return;


			//var settings = new CefSettings()
			//{
			//	BrowserSubprocessPath = Path.Combine(
			//			AppDomain.CurrentDomain.SetupInformation.ApplicationBase,
			//			Environment.Is64BitProcess ? "x64" : "x86",
			//			"CefSharp.BrowserSubprocess.exe"),
			//	CachePath = BrowserCachePath,
			//	Locale = "ja",
			//	AcceptLanguageList = "ja,en-US,en",        // todo: いる？
			//	LogSeverity = LogSeverity.Error,
			//	LogFile = "BrowserLog.log",
			//};

			//if (!Configuration.HardwareAccelerationEnabled)
			//	settings.DisableGpuAcceleration();

			//settings.CefCommandLineArgs.Add("proxy-server", ProxySettings);
			//if (Configuration.ForceColorProfile)
			//	settings.CefCommandLineArgs.Add("force-color-profile", "srgb");
			//CefSharpSettings.SubprocessExitIfParentProcessClosed = true;
			//Cef.Initialize(settings, false, null);


			//var requestHandler = new RequestHandler(pixiSettingEnabled: Configuration.PreserveDrawingBuffer);
			//requestHandler.RenderProcessTerminated += (mes) => AddLog(3, mes);

			Xpcom.Initialize("Firefox");

			GeckoPreferences.Default["network.proxy.type"] = 1;
			GeckoPreferences.Default["network.proxy.http"] = HttpProxyHost;
			GeckoPreferences.Default["network.proxy.http_port"] = HttpProxyPort;
			if (SslProxyPort != 0)
			{
				GeckoPreferences.Default["network.proxy.ssl"] = SslProxyHost;
				GeckoPreferences.Default["network.proxy.ssl_port"] = SslProxyPort;
			}

			if (!Configuration.HardwareAccelerationEnabled)
			{
				GeckoPreferences.Default["gfx.direct2d.disabled"] = true;
				GeckoPreferences.Default["layers.acceleration.disabled"] = true;
			}

			GeckoPreferences.Default["devtools.debugger.remote-enabled"] = true;

			Browser = new GeckoWebBrowser()
			{
				Dock = DockStyle.None,
				Size = SizeAdjuster.Size,
				//RequestHandler = requestHandler,
				//MenuHandler = new MenuHandler(),
				//KeyboardHandler = new KeyboardHandler(),
				//DragHandler = new DragHandler(),

			};

			Browser.DocumentCompleted += Browser_DocumentCompleted;
			//Browser.LoadingStateChanged += Browser_LoadingStateChanged;
			SizeAdjuster.Controls.Add(Browser);
		}

		void Exit()
		{
			if (!BrowserHost.Closed)
			{
				BrowserHost.Close();
				HeartbeatTimer.Stop();
				Xpcom.Shutdown();
				Application.Exit();
			}
		}

		void BrowserHostChannel_Faulted(Exception e)
		{
			// 親と通信できなくなったら終了する
			Exit();
		}

		public void CloseBrowser()
		{
			HeartbeatTimer.Stop();
			// リモートコールでClose()呼ぶのばヤバそうなので非同期にしておく
			BeginInvoke((Action)(() => Exit()));
		}

		public void ConfigurationChanged(BrowserLib.BrowserConfiguration conf)
		{
			Configuration = conf;

			SizeAdjuster.AutoScroll = Configuration.IsScrollable;
			ToolMenu_Other_Zoom_Fit.Checked = Configuration.ZoomFit;
			ApplyZoom();
			ToolMenu_Other_AppliesStyleSheet.Checked = Configuration.AppliesStyleSheet;
			ToolMenu.Dock = (DockStyle)Configuration.ToolMenuDockStyle;
			ToolMenu.Visible = Configuration.IsToolMenuVisible;

		}

		private void ConfigurationUpdated()
		{
			BrowserHost.AsyncRemoteRun(() => BrowserHost.Proxy.ConfigurationUpdated(Configuration));
		}

		private void AddLog(int priority, string message)
		{
			BrowserHost.AsyncRemoteRun(() => BrowserHost.Proxy.AddLog(priority, message));
		}

		private void SendErrorReport(string exceptionName, string message)
		{
			BrowserHost.AsyncRemoteRun(() => BrowserHost.Proxy.SendErrorReport(exceptionName, message));
		}


		public void InitialAPIReceived()
		{
			IsKanColleLoaded = true;

			//ロード直後の適用ではレイアウトがなぜか崩れるのでこのタイミングでも適用
			ApplyStyleSheet();
			ApplyZoom();
			DestroyDMMreloadDialog();

			//起動直後はまだ音声が鳴っていないのでミュートできないため、この時点で有効化
			SetVolumeState();
		}


		private void SizeAdjuster_SizeChanged(object sender, EventArgs e)
		{
			if (!StyleSheetApplied)
			{
				if (Browser != null)
				{
					Browser.Location = new Point(0, 0);
					Browser.Size = SizeAdjuster.Size;
				}
				return;
			}

			ApplyZoom();
		}

		private void CenteringBrowser()
		{
			if (SizeAdjuster.Width == 0 || SizeAdjuster.Height == 0) return;
			int x = Browser.Location.X, y = Browser.Location.Y;
			bool isScrollable = Configuration.IsScrollable;

			if (!isScrollable || Browser.Width <= SizeAdjuster.Width)
			{
				x = (SizeAdjuster.Width - Browser.Width) / 2;
			}
			if (!isScrollable || Browser.Height <= SizeAdjuster.Height)
			{
				y = (SizeAdjuster.Height - Browser.Height) / 2;
			}

			//if ( x != Browser.Location.X || y != Browser.Location.Y )
			Browser.Location = new Point(x, y);
		}


		private void Browser_DocumentCompleted(object sender, GeckoDocumentCompletedEventArgs e)
		{
			// DocumentCompleted に相当?
			// note: 非 UI thread からコールされるので、何かしら UI に触る場合は適切な処置が必要


			BeginInvoke((Action)(() =>
			{
				ApplyStyleSheet();

				ApplyZoom();
				DestroyDMMreloadDialog();
			}));
		}


		private bool IsBrowserInitialized =>
			Browser != null;

		private GeckoWindow GetMainFrame()
		{
			if (!IsBrowserInitialized)
				return null;

			if (Browser.Document?.Url?.AbsoluteUri.Contains(@"http://www.dmm.com/netgame/social/") ?? false)
				return Browser.Window;

			return null;
		}

		private GeckoIFrameElement GetGameFrame()
		{
			if (!IsBrowserInitialized)
				return null;

			var frames = Browser.Document?.GetElementsByTagName("iframe");

			return frames.Select(f => f as Gecko.DOM.GeckoIFrameElement)
				.FirstOrDefault(f => f?.Src.Contains(@"http://osapi.dmm.com/gadgets/") ?? false) ;
		}

		private GeckoDocument GetKanColleFrame()
		{
			if (!IsBrowserInitialized)
				return null;

			var frames = Browser.Document?.GetElementsByTagName("iframe");

			return frames.Select(f => f as Gecko.DOM.GeckoIFrameElement)
				.FirstOrDefault(f => f.Src?.Contains(@"/kcs2/index.php") ?? false)?.ContentDocument;
		}




		/// <summary>
		/// スタイルシートを適用します。
		/// </summary>
		public void ApplyStyleSheet()
		{
			if (!IsBrowserInitialized)
				return;

			if (!Configuration.AppliesStyleSheet && !RestoreStyleSheet)
				return;

			try
			{
				var mainframe = GetMainFrame();
				var gameframe = GetGameFrame();
				if (mainframe == null || gameframe == null)
					return;

				var context = new AutoJSContext(mainframe); 

				if (RestoreStyleSheet)
				{
					context.EvaluateScript(string.Format(GeckoBrowser.Properties.Resources.RestoreScript, StyleClassID));
					//context.EvaluateScript(string.Format(GeckoBrowser.Properties.Resources.RestoreScript, StyleClassID));
					StyleSheetApplied = false;
					RestoreStyleSheet = false;
				}
				else
				{ 
					context.EvaluateScript(string.Format(GeckoBrowser.Properties.Resources.PageScript, StyleClassID));
					//context.EvaluateScript(string.Format(GeckoBrowser.Properties.Resources.FrameScript, StyleClassID));
				}

				StyleSheetApplied = true;

			}
			catch (Exception ex)
			{
				SendErrorReport(ex.ToString(), "スタイルシートの適用に失敗しました。");
			}

		}

		/// <summary>
		/// DMMによるページ更新ダイアログを非表示にします。
		/// </summary>
		public void DestroyDMMreloadDialog()
		{
			if (!IsBrowserInitialized)
				return;

			if (!Configuration.IsDMMreloadDialogDestroyable)
				return;

			try
			{
				var mainframe = GetMainFrame();
				if (mainframe == null)
					return;

				var mainJs = new AutoJSContext(mainframe);

				mainJs.EvaluateScript(GeckoBrowser.Properties.Resources.DMMScript);
			}
			catch (Exception ex)
			{
				SendErrorReport(ex.ToString(), "DMMによるページ更新ダイアログの非表示に失敗しました。");
			}

		}

		/// <summary>
		/// 指定した URL のページを開きます。
		/// </summary>
		public void Navigate(string url)
		{
			if (url != Configuration.LogInPageURL || !Configuration.AppliesStyleSheet)
				StyleSheetApplied = false;
			Browser.Navigate(url);
		}

		/// <summary>
		/// ブラウザを再読み込みします。
		/// </summary>
		public void RefreshBrowser() => RefreshBrowser(false);

		/// <summary>
		/// ブラウザを再読み込みします。
		/// </summary>
		/// <param name="ignoreCache">キャッシュを無視するか。</param>
		public void RefreshBrowser(bool ignoreCache)
		{
			if (!Configuration.AppliesStyleSheet)
				StyleSheetApplied = false;

			if (ignoreCache)
			{
				Browser.Reload(GeckoLoadFlags.BypassCache);
			}
			else
			{
				Browser.Reload();
			}
		}

		/// <summary>
		/// ズームを適用します。
		/// </summary>
		public void ApplyZoom()
		{
			if (!IsBrowserInitialized)
				return;


			double zoomRate = Configuration.ZoomRate;
			bool fit = Configuration.ZoomFit && StyleSheetApplied;


			double zoomFactor;

			if (fit)
			{
				double rateX = (double)SizeAdjuster.Width / KanColleSize.Width;
				double rateY = (double)SizeAdjuster.Height / KanColleSize.Height;
				zoomFactor = Math.Min(rateX, rateY);
			}
			else
			{
				if (zoomRate < 0.1)
					zoomRate = 0.1;
				if (zoomRate > 10)
					zoomRate = 10;

				zoomFactor = zoomRate;
			}

			Browser.GetDocShellAttribute().GetContentViewerAttribute().SetFullZoomAttribute((float) zoomFactor);


			if (StyleSheetApplied)
			{
				Browser.Size = Browser.MinimumSize = new Size(
					(int)(KanColleSize.Width * zoomFactor),
					(int)(KanColleSize.Height * zoomFactor)
					);

				CenteringBrowser();
			}

			if (fit)
			{
				ToolMenu_Other_Zoom_Current.Text = "現在: ぴったり";
			}
			else
			{
				ToolMenu_Other_Zoom_Current.Text = $"現在: {zoomRate:p1}";
			}

		}



		/// <summary>
		/// スクリーンショットを撮影します。
		/// </summary>
		private async Task<Bitmap> TakeScreenShot()
		{
			return null;
//			var kancolleFrame = GetKanColleFrame();
//			if (kancolleFrame == null)
//			{
//				AddLog(3, string.Format("艦これが読み込まれていないため、スクリーンショットを撮ることはできません。"));
//				System.Media.SystemSounds.Beep.Play();
//				return null;
//			}


//			Task<ScreenShotPacket> InternalTakeScreenShot()
//			{
//				var request = new ScreenShotPacket();

//				if (Browser == null || !Browser.IsBrowserInitialized)
//					return request.TaskSource.Task;


//				string script = $@"
//(async function() 
//{{
//	await CefSharp.BindObjectAsync('{request.ID}');

//	let canvas = document.querySelector('canvas');
//	requestAnimationFrame(() =>
//	{{
//		let dataurl = canvas.toDataURL('image/png');
//		{request.ID}.complete(dataurl);
//	}});
//}})();
//";

//				Browser.JavascriptObjectRepository.Register(request.ID, request, true);
//				kancolleFrame.ExecuteJavaScriptAsync(script);

//				return request.TaskSource.Task;
//			}

//			var result = await InternalTakeScreenShot();

//			// ごみ掃除
//			Browser.JavascriptObjectRepository.UnRegister(result.ID);
//			kancolleFrame.ExecuteJavaScriptAsync($@"delete {result.ID}");

//			return result.GetImage();
		}



		/// <summary>
		/// スクリーンショットを撮影し、設定で指定された保存先に保存します。
		/// </summary>
		public async Task SaveScreenShot()
		{

			int savemode = Configuration.ScreenShotSaveMode;
			int format = Configuration.ScreenShotFormat;
			string folderPath = Configuration.ScreenShotPath;
			bool is32bpp = format != 1 && Configuration.AvoidTwitterDeterioration;

			Bitmap image = null;
			try
			{
				image = await TakeScreenShot();


				if (image == null)
					return;

				if (is32bpp)
				{
					if (image.PixelFormat != PixelFormat.Format32bppArgb)
					{
						var imgalt = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
						using (var g = Graphics.FromImage(imgalt))
						{
							g.DrawImage(image, new Rectangle(0, 0, imgalt.Width, imgalt.Height));
						}

						image.Dispose();
						image = imgalt;
					}

					// 不透明ピクセルのみだと jpeg 化されてしまうため、1px だけわずかに透明にする
					Color temp = image.GetPixel(image.Width - 1, image.Height - 1);
					image.SetPixel(image.Width - 1, image.Height - 1, Color.FromArgb(252, temp.R, temp.G, temp.B));
				}
				else
				{
					if (image.PixelFormat != PixelFormat.Format24bppRgb)
					{
						var imgalt = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
						using (var g = Graphics.FromImage(imgalt))
						{
							g.DrawImage(image, new Rectangle(0, 0, imgalt.Width, imgalt.Height));
						}

						image.Dispose();
						image = imgalt;
					}
				}


				// to file
				if ((savemode & 1) != 0)
				{
					try
					{
						if (!Directory.Exists(folderPath))
							Directory.CreateDirectory(folderPath);

						string ext;
						ImageFormat imgFormat;

						switch (format)
						{
							case 1:
								ext = "jpg";
								imgFormat = ImageFormat.Jpeg;
								break;
							case 2:
							default:
								ext = "png";
								imgFormat = ImageFormat.Png;
								break;
						}

						string path = $"{folderPath}\\{DateTime.Now:yyyyMMdd_HHmmssff}.{ext}";
						image.Save(path, imgFormat);
						_lastScreenShotPath = path;

						AddLog(2, $"スクリーンショットを {path} に保存しました。");
					}
					catch (Exception ex)
					{
						SendErrorReport(ex.ToString(), "スクリーンショットの保存に失敗しました。");
					}
				}


				// to clipboard
				if ((savemode & 2) != 0)
				{
					try
					{
						Clipboard.SetImage(image);

						if ((savemode & 3) != 3)
							AddLog(2, "スクリーンショットをクリップボードにコピーしました。");
					}
					catch (Exception ex)
					{
						SendErrorReport(ex.ToString(), "スクリーンショットのクリップボードへのコピーに失敗しました。");
					}
				}

			}
			catch (Exception ex)
			{
				SendErrorReport(ex.ToString(), "スクリーンショットの撮影に失敗しました。");
			}
			finally
			{
				image?.Dispose();
			}

		}


		public void SetProxy(string proxy)
		{
			ushort port;
			if (ushort.TryParse(proxy, out port))
			{
				//WinInetUtil.SetProxyInProcessForNekoxy(port);
				HttpProxyHost = "127.0.0.1";
				HttpProxyPort = port;
				//ProxySettings = "http=127.0.0.1:" + port;           // todo: 動くには動くが正しいかわからない
			}
			else
			{
				//WinInetUtil.SetProxyInProcess(proxy, "local");
				Regex regexWithUpstream = new Regex(@"^http=127\.0\.0\.1\:(\d+);https=(.+)\:(\d+)$");
				var match = regexWithUpstream.Match(proxy);
				if (match.Success)
				{
					HttpProxyHost = "127.0.0.1";
					HttpProxyPort = Convert.ToInt32(match.Groups[1].Value);
					SslProxyHost = match.Groups[2].Value;
					SslProxyPort = Convert.ToInt32(match.Groups[3].Value);
				}

				Regex regexWithoutUpstream = new Regex(@"^http=127\.0\.0\.1\:(\d+)$");
				var match2 = regexWithoutUpstream.Match(proxy);
				if (match2.Success)
				{
					HttpProxyHost = "127.0.0.1";
					HttpProxyPort = Convert.ToInt32(match2.Groups[1].Value);
				}
				//ProxySettings = proxy;
			}

			InitializeBrowser();

			BrowserHost.AsyncRemoteRun(() => BrowserHost.Proxy.SetProxyCompleted());
		}


		/// <summary>
		/// キャッシュを削除します。
		/// </summary>
		private bool ClearCache(long timeoutMilliseconds = 5000)
		{
			// note: Cef が起動している状態では削除できない X(
			// 今のところ手動でやってもらうことにする

			return true;
		}


		public void SetIconResource(byte[] canvas)
		{

			string[] keys = new string[] {
				"Browser_ScreenShot",
				"Browser_Zoom",
				"Browser_ZoomIn",
				"Browser_ZoomOut",
				"Browser_Unmute",
				"Browser_Mute",
				"Browser_Refresh",
				"Browser_Navigate",
				"Browser_Other",
			};
			int unitsize = 16 * 16 * 4;

			for (int i = 0; i < keys.Length; i++)
			{
				Bitmap bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);

				if (canvas != null)
				{
					BitmapData bmpdata = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
					Marshal.Copy(canvas, unitsize * i, bmpdata.Scan0, unitsize);
					bmp.UnlockBits(bmpdata);
				}

				Icons.Images.Add(keys[i], bmp);
			}


			ToolMenu_ScreenShot.Image = ToolMenu_Other_ScreenShot.Image =
				Icons.Images["Browser_ScreenShot"];
			ToolMenu_Zoom.Image = ToolMenu_Other_Zoom.Image =
				Icons.Images["Browser_Zoom"];
			ToolMenu_Other_Zoom_Increment.Image =
				Icons.Images["Browser_ZoomIn"];
			ToolMenu_Other_Zoom_Decrement.Image =
				Icons.Images["Browser_ZoomOut"];
			ToolMenu_Refresh.Image = ToolMenu_Other_Refresh.Image =
				Icons.Images["Browser_Refresh"];
			ToolMenu_NavigateToLogInPage.Image = ToolMenu_Other_NavigateToLogInPage.Image =
				Icons.Images["Browser_Navigate"];
			ToolMenu_Other.Image =
				Icons.Images["Browser_Other"];

			SetVolumeState();
		}

		public void SendMouseEvent(string type, double x, double y)
		{
		}

		public byte[] TakeScreenShotAsPngBytes()
		{
			return new byte[] { };
		}


		private void SetVolumeState()
		{

			bool mute;
			float volume;

			try
			{
				mute = _volumeManager.IsMute;
				volume = _volumeManager.Volume * 100;

			}
			catch (Exception)
			{
				// 音量データ取得不能時
				mute = false;
				volume = 100;
			}

			ToolMenu_Mute.Image = ToolMenu_Other_Mute.Image =
				Icons.Images[mute ? "Browser_Mute" : "Browser_Unmute"];

			{
				var control = ToolMenu_Other_Volume_VolumeControl;
				control.Tag = false;
				control.Value = (decimal)volume;
				control.Tag = true;
			}

			Configuration.Volume = volume;
			Configuration.IsMute = mute;
			ConfigurationUpdated();
		}


		private async void ToolMenu_Other_ScreenShot_Click(object sender, EventArgs e)
		{
			await SaveScreenShot();
		}

		private void ToolMenu_Other_Zoom_Decrement_Click(object sender, EventArgs e)
		{
			Configuration.ZoomRate = Math.Max(Configuration.ZoomRate - 0.2, 0.1);
			Configuration.ZoomFit = ToolMenu_Other_Zoom_Fit.Checked = false;
			ApplyZoom();
			ConfigurationUpdated();
		}

		private void ToolMenu_Other_Zoom_Increment_Click(object sender, EventArgs e)
		{
			Configuration.ZoomRate = Math.Min(Configuration.ZoomRate + 0.2, 10);
			Configuration.ZoomFit = ToolMenu_Other_Zoom_Fit.Checked = false;
			ApplyZoom();
			ConfigurationUpdated();
		}

		private void ToolMenu_Other_Zoom_Click(object sender, EventArgs e)
		{

			double zoom;

			if (sender == ToolMenu_Other_Zoom_25)
				zoom = 0.25;
			else if (sender == ToolMenu_Other_Zoom_50)
				zoom = 0.50;
			else if (sender == ToolMenu_Other_Zoom_Classic)
				zoom = 0.667;       // 2/3 ジャストだと 799x479 になる
			else if (sender == ToolMenu_Other_Zoom_75)
				zoom = 0.75;
			else if (sender == ToolMenu_Other_Zoom_100)
				zoom = 1;
			else if (sender == ToolMenu_Other_Zoom_150)
				zoom = 1.5;
			else if (sender == ToolMenu_Other_Zoom_200)
				zoom = 2;
			else if (sender == ToolMenu_Other_Zoom_250)
				zoom = 2.5;
			else if (sender == ToolMenu_Other_Zoom_300)
				zoom = 3;
			else if (sender == ToolMenu_Other_Zoom_400)
				zoom = 4;
			else
				zoom = 1;

			Configuration.ZoomRate = zoom;
			Configuration.ZoomFit = ToolMenu_Other_Zoom_Fit.Checked = false;
			ApplyZoom();
			ConfigurationUpdated();
		}

		private void ToolMenu_Other_Zoom_Fit_Click(object sender, EventArgs e)
		{
			Configuration.ZoomFit = ToolMenu_Other_Zoom_Fit.Checked;
			ApplyZoom();
			ConfigurationUpdated();
		}


		//ズームUIの使いまわし
		private void ToolMenu_Other_DropDownOpening(object sender, EventArgs e)
		{
			var list = ToolMenu_Zoom.DropDownItems.Cast<ToolStripItem>().ToArray();
			ToolMenu_Other_Zoom.DropDownItems.AddRange(list);
		}

		private void ToolMenu_Zoom_DropDownOpening(object sender, EventArgs e)
		{

			var list = ToolMenu_Other_Zoom.DropDownItems.Cast<ToolStripItem>().ToArray();
			ToolMenu_Zoom.DropDownItems.AddRange(list);
		}


		private void ToolMenu_Other_Mute_Click(object sender, EventArgs e)
		{
			try
			{
				_volumeManager.ToggleMute();

			}
			catch (Exception)
			{
				System.Media.SystemSounds.Beep.Play();
			}

			SetVolumeState();
		}

		void ToolMenu_Other_Volume_ValueChanged(object sender, EventArgs e)
		{

			var control = ToolMenu_Other_Volume_VolumeControl;

			try
			{
				if ((bool)control.Tag)
					_volumeManager.Volume = (float)(control.Value / 100);
				control.BackColor = SystemColors.Window;

			}
			catch (Exception)
			{
				control.BackColor = Color.MistyRose;

			}

		}


		private void ToolMenu_Other_Refresh_Click(object sender, EventArgs e)
		{

			if (!Configuration.ConfirmAtRefresh ||
				MessageBox.Show("再読み込みします。\r\nよろしいですか？", "確認",
				MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button2)
				== DialogResult.OK)
			{
				RefreshBrowser();
			}
		}

		private void ToolMenu_Other_RefreshIgnoreCache_Click(object sender, EventArgs e)
		{
			if (!Configuration.ConfirmAtRefresh ||
				MessageBox.Show("キャッシュを無視して再読み込みします。\r\nよろしいですか？", "確認",
				MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button2)
				== DialogResult.OK)
			{
				RefreshBrowser(true);
			}
		}

		private void ToolMenu_Other_NavigateToLogInPage_Click(object sender, EventArgs e)
		{

			if (MessageBox.Show("ログインページへ移動します。\r\nよろしいですか？", "確認",
				MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2)
				== DialogResult.OK)
			{

				Navigate(Configuration.LogInPageURL);
			}

		}

		private void ToolMenu_Other_Navigate_Click(object sender, EventArgs e)
		{
			var currentUrl = Browser.Document?.Url?.AbsoluteUri ?? "";
			BrowserHost.AsyncRemoteRun(() => BrowserHost.Proxy.RequestNavigation(currentUrl));
		}

		private void ToolMenu_Other_AppliesStyleSheet_Click(object sender, EventArgs e)
		{
			Configuration.AppliesStyleSheet = ToolMenu_Other_AppliesStyleSheet.Checked;
			if (!Configuration.AppliesStyleSheet)
				RestoreStyleSheet = true;
			ApplyStyleSheet();
			ApplyZoom();
			ConfigurationUpdated();
		}

		private void ToolMenu_Other_Alignment_Click(object sender, EventArgs e)
		{

			if (sender == ToolMenu_Other_Alignment_Top)
				ToolMenu.Dock = DockStyle.Top;
			else if (sender == ToolMenu_Other_Alignment_Bottom)
				ToolMenu.Dock = DockStyle.Bottom;
			else if (sender == ToolMenu_Other_Alignment_Left)
				ToolMenu.Dock = DockStyle.Left;
			else
				ToolMenu.Dock = DockStyle.Right;

			Configuration.ToolMenuDockStyle = (int)ToolMenu.Dock;

			ConfigurationUpdated();
		}

		private void ToolMenu_Other_Alignment_Invisible_Click(object sender, EventArgs e)
		{
			ToolMenu.Visible =
			Configuration.IsToolMenuVisible = false;
			ConfigurationUpdated();
		}



		private void SizeAdjuster_DoubleClick(object sender, EventArgs e)
		{
			ToolMenu.Visible =
			Configuration.IsToolMenuVisible = true;
			ConfigurationUpdated();
		}

		private void ContextMenuTool_ShowToolMenu_Click(object sender, EventArgs e)
		{
			ToolMenu.Visible =
			Configuration.IsToolMenuVisible = true;
			ConfigurationUpdated();
		}

		private void ContextMenuTool_Opening(object sender, CancelEventArgs e)
		{

			if (IsKanColleLoaded || ToolMenu.Visible)
				e.Cancel = true;
		}


		private void ToolMenu_ScreenShot_Click(object sender, EventArgs e)
		{
			ToolMenu_Other_ScreenShot_Click(sender, e);
		}

		private void ToolMenu_Mute_Click(object sender, EventArgs e)
		{
			ToolMenu_Other_Mute_Click(sender, e);
		}

		private void ToolMenu_Refresh_Click(object sender, EventArgs e)
		{
			ToolMenu_Other_Refresh_Click(sender, e);
		}

		private void ToolMenu_NavigateToLogInPage_Click(object sender, EventArgs e)
		{
			ToolMenu_Other_NavigateToLogInPage_Click(sender, e);
		}




		private void FormBrowser_Activated(object sender, EventArgs e)
		{
			Browser.Focus();
		}

		private void ToolMenu_Other_Alignment_DropDownOpening(object sender, EventArgs e)
		{

			foreach (var item in ToolMenu_Other_Alignment.DropDownItems)
			{
				var menu = item as ToolStripMenuItem;
				if (menu != null)
				{
					menu.Checked = false;
				}
			}

			switch ((DockStyle)Configuration.ToolMenuDockStyle)
			{
				case DockStyle.Top:
					ToolMenu_Other_Alignment_Top.Checked = true;
					break;
				case DockStyle.Bottom:
					ToolMenu_Other_Alignment_Bottom.Checked = true;
					break;
				case DockStyle.Left:
					ToolMenu_Other_Alignment_Left.Checked = true;
					break;
				case DockStyle.Right:
					ToolMenu_Other_Alignment_Right.Checked = true;
					break;
			}

			ToolMenu_Other_Alignment_Invisible.Checked = !Configuration.IsToolMenuVisible;
		}


		private void ToolMenu_Other_LastScreenShot_DropDownOpening(object sender, EventArgs e)
		{

			try
			{

				using (var fs = new FileStream(_lastScreenShotPath, FileMode.Open, FileAccess.Read))
				{
					ToolMenu_Other_LastScreenShot_Control.Image?.Dispose();

					ToolMenu_Other_LastScreenShot_Control.Image = Image.FromStream(fs);
				}

			}
			catch (Exception)
			{
				// *ぷちっ*
			}

		}

		void ToolMenu_Other_LastScreenShot_ImageHost_Click(object sender, EventArgs e)
		{
			if (_lastScreenShotPath != null && File.Exists(_lastScreenShotPath))
				Process.Start(_lastScreenShotPath);
		}

		private void ToolMenu_Other_LastScreenShot_OpenScreenShotFolder_Click(object sender, EventArgs e)
		{
			if (Directory.Exists(Configuration.ScreenShotPath))
				Process.Start(Configuration.ScreenShotPath);
		}

		private void ToolMenu_Other_LastScreenShot_CopyToClipboard_Click(object sender, EventArgs e)
		{

			if (_lastScreenShotPath != null && File.Exists(_lastScreenShotPath))
			{
				try
				{
					using (var img = new Bitmap(_lastScreenShotPath))
					{
						Clipboard.SetImage(img);
						AddLog(2, string.Format("スクリーンショット {0} をクリップボードにコピーしました。", _lastScreenShotPath));
					}
				}
				catch (Exception ex)
				{
					SendErrorReport(ex.Message, "スクリーンショットのクリップボードへのコピーに失敗しました。");
				}
			}
		}

		private void ToolMenu_Other_OpenDevTool_Click(object sender, EventArgs e)
		{
			if (!IsBrowserInitialized)
				return;


			//Browser.GetBrowser().ShowDevTools();
		}


		protected override void WndProc(ref Message m)
		{

			if (m.Msg == WM_ERASEBKGND)
				// ignore this message
				return;

			base.WndProc(ref m);
		}


		#region 呪文


		[DllImport("user32.dll", EntryPoint = "GetWindowLongA", SetLastError = true)]
		private static extern uint GetWindowLong(IntPtr hwnd, int nIndex);

		[DllImport("user32.dll", EntryPoint = "SetWindowLongA", SetLastError = true)]
		private static extern uint SetWindowLong(IntPtr hwnd, int nIndex, uint dwNewLong);

		private const int GWL_STYLE = (-16);
		private const uint WS_CHILD = 0x40000000;
		private const uint WS_VISIBLE = 0x10000000;
		private const int WM_ERASEBKGND = 0x14;




		#endregion


	}



	/// <summary>
	/// ウィンドウが非アクティブ状態から1回のクリックでボタンが押せる ToolStrip です。
	/// </summary>
	internal class ExtraToolStrip : ToolStrip
	{
		public ExtraToolStrip() : base() { }

		private const uint WM_MOUSEACTIVATE = 0x21;
		private const uint MA_ACTIVATE = 1;
		private const uint MA_ACTIVATEANDEAT = 2;
		private const uint MA_NOACTIVATE = 3;
		private const uint MA_NOACTIVATEANDEAT = 4;

		protected override void WndProc(ref Message m)
		{
			base.WndProc(ref m);

			if (m.Msg == WM_MOUSEACTIVATE && m.Result == (IntPtr)MA_ACTIVATEANDEAT)
				m.Result = (IntPtr)MA_ACTIVATE;
		}
	}

}
