using Microsoft.Win32;
using ScottPlot;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace XPSUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            graph_Default_setting();
            graph_1.Refresh();
            
        }
        //グローバル関数の定義
        public string data_file_path=" ";

        //デフォルトプロット
        private void graph_Default_setting()
        {
            var x_label = graph_1.Plot.Axes.Bottom.Label;
            var x_axe = graph_1.Plot.Axes.Bottom;
            var y_label = graph_1.Plot.Axes.Left.Label;
            var y_axe = graph_1.Plot.Axes.Left;

            x_label.Text = "Binding Energy [eV]";
            x_label.FontSize = 24;
            x_label.FontName = "Times New Roman";
            x_axe.TickLabelStyle.FontSize = 24;
            x_axe.TickLabelStyle.FontName = "Times New Roman";

            y_label.Text = "Intensity [-]";
            y_label.FontSize = 24;
            y_label.FontName = "Times New Roman";
            y_axe.TickLabelStyle.FontSize = 24;
            y_axe.TickLabelStyle.FontName = "Times New Roman";

            //デバック用
            double[] dataX = { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            double[] dataY = { 1, 4, 9, 16, 25, 16, 9, 4, 1 };
            graph_1.Plot.Add.Scatter(dataX, dataY);
        }

        
        //ファイルパス獲得
        private void Open_file(object sender, RoutedEventArgs e)
        {
            // 1. ファイルダイアログを作成
            var dialog = new OpenFileDialog();

            // フィルター設定（例: テキストファイルか全てのファイル）
            dialog.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";

            // 2. ダイアログを表示し、OKが押されたか確認
            if (dialog.ShowDialog() == true)
            {
                // 選択されたファイルのフルパスを取得
                string selectedPath = dialog.FileName;
                data_file_path = selectedPath;
                // 画面に表示したい場合（例: TextBoxなどがあれば）デバック用
                DebagBox.Text = data_file_path;

            }

        }

        private void OpenSettings_shift(object sender, RoutedEventArgs e)
        {
            // 1. 設定ウィンドウのインスタンス（実体）を作る
            var Window1 = new Window1();

            // 2. ウィンドウを表示する
            // Show() ではなく ShowDialog() を使うと、
            // 設定画面を閉じるまでメイン画面が操作できなくなります（設定画面向き）
            Window1.ShowDialog();
        }

        private void OpenSetting_RSF(object sender, RoutedEventArgs e)
        {
            var RSF=new RSF();

            RSF.ShowDialog();
        }

        private void OpenSetting_peakfit(object sender, RoutedEventArgs e)
        {
            var PeakFitEditor = new PeakFitEditor();

            PeakFitEditor.ShowDialog();
        }

    }
}
        
    