using Microsoft.Win32;
using ScottPlot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using static XPSUI.XpsEngine;

namespace XPSUI
{
    public partial class MainWindow : Window
    {
        // ロジックを担当するエンジンを呼び出す
        private XpsEngine _engine = new XpsEngine();

        public MainWindow()
        {
            InitializeComponent();

            // Python初期化 (失敗してもここでは静かにする)
            _engine.TryInitializePython(silent: true);

            // 初期グラフ設定
            graph_Default_setting();
            graph_1.Refresh();
        }

        // ---------------------------------------------------------
        // メニュー: Pythonパス設定
        // ---------------------------------------------------------
        private void OpenSetting_Python(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Python DLL (python3xx.dll)",
                Filter = "Python DLL|python3*.dll|All Files|*.*",
                FileName = "python310.dll"
            };

            if (dialog.ShowDialog() == true)
            {
                // エンジンに保存させる
                _engine.SavePythonPath(dialog.FileName);

                try
                {
                    // 初期化再試行
                    if (_engine.TryInitializePython(silent: false))
                    {
                        MessageBox.Show($"設定保存＆Python起動成功！\nPath: {dialog.FileName}");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"起動失敗: {ex.Message}\n再起動してください。");
                }
            }
        }

        // ---------------------------------------------------------
        // ファイルを開く
        // ---------------------------------------------------------
        private void Open_file(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "ASCII/CSV Files (*.asc;*.csv;*.txt)|*.asc;*.csv;*.txt|All Files (*.*)|*.*",
                Title = "データファイルを開く"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // エンジンに読み込ませる
                    _engine.LoadData(dialog.FileName);

                    if (_engine.Tags.Count == 0)
                    {
                        MessageBox.Show("データが見つかりませんでした。");
                        return;
                    }

                    // 画面更新
                    UpdateGraph();
                    UpdateTable();

                    MessageBox.Show("読み込み完了！");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"読み込みエラー:\n{ex.Message}\nPython設定を確認してください。");
                }
            }
        }

        // ---------------------------------------------------------
        // 帯電補正 (Shift)
        // ---------------------------------------------------------
        private void ApplyShift_Click(object sender, RoutedEventArgs e)
        {
            if (_engine.Tags.Count == 0)
            {
                MessageBox.Show("データがありません。");
                return;
            }

            try
            {
                // エンジンを使って設定読み込み -> 補正実行
                var settings = _engine.LoadShiftSettings();
                _engine.ApplyShift(settings);

                UpdateGraph();

                MessageBox.Show($"補正完了 (Ref: {settings.ShiftPeakCenter} eV)");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"補正エラー:\n{ex.Message}");
            }
        }

        // ---------------------------------------------------------
        // UI更新メソッド (データは _engine からもらう)
        // ---------------------------------------------------------
        private void UpdateGraph()
        {
            graph_1.Plot.Clear();

            for (int i = 0; i < _engine.Tags.Count; i++)
            {
                var scatter = graph_1.Plot.Add.Scatter(_engine.XData[i], _engine.YData[i]);
                scatter.LegendText = _engine.Tags[i];
                scatter.MarkerSize = 2;
                scatter.LineWidth = 1;
            }

            if (_engine.XData.Count > 0)
            {
                double xMax = _engine.XData.SelectMany(x => x).Max();
                double xMin = _engine.XData.SelectMany(x => x).Min();
                graph_1.Plot.Axes.SetLimitsX(xMax, xMin);
            }

            graph_1.Plot.ShowLegend();
            graph_1.Plot.Axes.AutoScale();
            graph_1.Refresh();
        }

        private void UpdateTable()
        {
            var tableData = new List<SpectrumInfo>();
            for (int i = 0; i < _engine.Tags.Count; i++)
            {
                tableData.Add(new SpectrumInfo
                {
                    Id = i + 1,
                    TagName = _engine.Tags[i],
                    Points = _engine.XData[i].Length
                });
            }
            SideDataGrid.ItemsSource = tableData;
        }

        private void graph_Default_setting()
        {
            graph_1.Plot.Axes.Bottom.Label.Text = "Binding Energy [eV]";
            graph_1.Plot.Axes.Bottom.Label.FontSize = 24;
            graph_1.Plot.Axes.Left.Label.Text = "Intensity [-]";
            graph_1.Plot.Axes.Left.Label.FontSize = 24;
            graph_1.Plot.Axes.Bottom.TickLabelStyle.FontSize = 18;
            graph_1.Plot.Axes.Left.TickLabelStyle.FontSize = 18;
        }

        // 設定画面を開くだけの処理
        private void OpenSettings_shift(object sender, RoutedEventArgs e) => new Window1().ShowDialog();
        private void OpenSetting_RSF(object sender, RoutedEventArgs e) => new RSF().ShowDialog();
        private void OpenSetting_peakfit(object sender, RoutedEventArgs e) => new PeakFitEditor().ShowDialog();

    }


    public class SpectrumInfo
    {
        public int Id { get; set; }
        public string TagName { get; set; } = "";
        public int Points { get; set; }
    }
}