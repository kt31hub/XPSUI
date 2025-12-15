using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace XPSUI
{
    public partial class PeakFitEditor : Window
    {
        // データを管理するリスト
        private ObservableCollection<PeakFitItem> _peakList = new ObservableCollection<PeakFitItem>();

        // 保存ファイル名
        private const string JsonFileName = "peakfit.json";

        public PeakFitEditor()
        {
            InitializeComponent();
            LoadData();
        }

        // --- パス取得 ---
        private string GetFilePath()
        {
            string myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string folderPath = System.IO.Path.Combine(myDocuments, "XPSUI_setting");

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            return System.IO.Path.Combine(folderPath, JsonFileName);
        }

        // --- 読み込み ---
        private void LoadData()
        {
            try
            {
                string path = GetFilePath();

                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var loadedData = JsonSerializer.Deserialize<ObservableCollection<PeakFitItem>>(json);
                    if (loadedData != null) _peakList = loadedData;
                }
                else
                {
                    // ★ファイルがない場合のデフォルトデータ (提示されたJSONの内容)
                    _peakList.Add(new PeakFitItem { id = "1", level = "C1s", name = "C-C", center = 284.8, center_error = 0.5, FWHM = 1.0, FWHM_error = 0.3 });
                    _peakList.Add(new PeakFitItem { id = "2", level = "C1s", name = "C-O", center = 286.3, center_error = 0.8, FWHM = 1.2, FWHM_error = 0.4 });
                    _peakList.Add(new PeakFitItem { id = "3", level = "C1s", name = "C=O", center = 288.0, center_error = 0.8, FWHM = 1.2, FWHM_error = 0.4 });
                    _peakList.Add(new PeakFitItem { id = "4", level = "O1s", name = "Cu-O", center = 529.8, center_error = 0.6, FWHM = 1.1, FWHM_error = 0.3 });
                    _peakList.Add(new PeakFitItem { id = "5", level = "O1s", name = "O-H", center = 531.5, center_error = 0.8, FWHM = 1.3, FWHM_error = 0.4 });
                    _peakList.Add(new PeakFitItem { id = "6", level = "O1s", name = "C-O", center = 533.0, center_error = 0.8, FWHM = 1.3, FWHM_error = 0.4 });
                    _peakList.Add(new PeakFitItem { id = "7", level = "Cu2p3", name = "Cu2O", center = 932.6, center_error = 0.4, FWHM = 1.1, FWHM_error = 0.3 });
                    _peakList.Add(new PeakFitItem { id = "8", level = "Cu2p3", name = "Cu", center = 932.7, center_error = 0.4, FWHM = 1.1, FWHM_error = 0.3 });
                    _peakList.Add(new PeakFitItem { id = "9", level = "Cu2p3", name = "CuO", center = 933.8, center_error = 0.8, FWHM = 1.8, FWHM_error = 0.6 });
                    _peakList.Add(new PeakFitItem { id = "10", level = "Cu2p3", name = "Cu(OH)2", center = 935.0, center_error = 0.8, FWHM = 1.8, FWHM_error = 0.6 });
                }

                PeakFitDataGrid.ItemsSource = _peakList;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"読み込みエラー: {ex.Message}");
            }
        }

        // --- 追加 ---
        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            // 1. 現在の最大IDを探す (リストが空なら0)
            int maxId = 0;

            foreach (var item in _peakList)
            {
                // 文字列のIDを数値に変換して比較
                if (int.TryParse(item.id, out int currentId))
                {
                    if (currentId > maxId)
                    {
                        maxId = currentId;
                    }
                }
            }

            // 2. 次の番号を決める (最大値 + 1)
            int nextId = maxId + 1;

            // 3. 新しい行を追加
            _peakList.Add(new PeakFitItem
            {
                id = nextId.ToString(), // 文字列に戻してセット
                level = "New",
                name = "New Peak",
                center = 0.0,
                FWHM = 1.0
            });
        }

        // --- 削除 ---
        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (PeakFitDataGrid.SelectedItem is PeakFitItem selectedItem)
            {
                _peakList.Remove(selectedItem);
            }
            else
            {
                MessageBox.Show("削除する行を選択してください。");
            }
        }

        // --- 保存 ---
        // --- 保存ボタンの処理 ---
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var item in _peakList)
                {
                    // 1. Level または Name に何か文字が入っているか確認
                    bool hasText = !string.IsNullOrWhiteSpace(item.level) || !string.IsNullOrWhiteSpace(item.name);

                    if (hasText)
                    {
                        // 2. 数値が 0 (またはマイナス) のままになっていないか確認
                        // ※XPSのピーク位置や半値幅(FWHM)が 0 になることは物理的にありえないため
                        if (item.center <= 0 || item.FWHM <= 0)
                        {
                            MessageBox.Show(
                                $"ID: {item.id} の行の数値が正しくありません。\n" +
                                $"Center や FWHM が 0、または未入力になっています。\n" +
                                $"(Level: {item.level}, Name: {item.name})",
                                "保存エラー",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);

                            // ここで処理を中断（保存させない）
                            return;
                        }
                    }
                }


                // --- 以下はこれまでの保存処理と同じ ---
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(_peakList, options);

                string path = GetFilePath();
                File.WriteAllText(path, jsonString);

                MessageBox.Show($"保存しました！\n場所: {path}", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // --- データ構造クラス (JSONキーと完全一致させる) ---
    public class PeakFitItem
    {
        public string id { get; set; } = "0";
        public string level { get; set; } = "";
        public string name { get; set; } = "";

        // 数値型 (double)
        public double center { get; set; } = 0.0;
        public double center_error { get; set; } = 0.5;
        public double FWHM { get; set; } = 1.0;
        public double FWHM_error { get; set; } = 0.3;
    }
}
