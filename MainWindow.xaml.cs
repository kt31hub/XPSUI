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

        public class AppSettings
        {
            // 保存したいパス
            public string LastFilePath { get; set; }
        }

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

                // 画面に表示したい場合（例: TextBoxなどがあれば）
                DebagBox.Text = selectedPath;

            }
        }

        

    }
}
        
    