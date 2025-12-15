using System;
using System.Collections.ObjectModel; // ObservableCollection用
using System.IO;            // ファイル操作用
using System.Text.Json;     // JSON操作用
using System.Windows;       // WPF基本機能

namespace XPSUI
{
    // クラス名を XAML の x:Class="XPSUI.RSF" に合わせる
    public partial class RSF : Window
    {
        // データを管理するリスト (画面と自動連動)
        private ObservableCollection<RsfItem> _rsfList = new ObservableCollection<RsfItem>();

        // 保存するファイル名
        private const string JsonFileName = "RSF.json";

        public RSF()
        {
            InitializeComponent(); // これでエラーは消えるはずです
            LoadData();            // 起動時にデータを読み込む
        }

        // --- 保存先パス取得 (マイドキュメント/XPSUI_setting/RSF.json) ---
        private string GetFilePath()
        {
            string myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string folderPath = System.IO.Path.Combine(myDocuments, "XPSUI_setting");

            // フォルダが無ければ作成
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            return System.IO.Path.Combine(folderPath, JsonFileName);
        }

        // --- データの読み込み ---
        private void LoadData()
        {
            try
            {
                string path = GetFilePath();

                if (File.Exists(path))
                {
                    // ファイルがあれば読み込んでリストにする
                    string json = File.ReadAllText(path);
                    var loadedData = JsonSerializer.Deserialize<ObservableCollection<RsfItem>>(json);

                    if (loadedData != null)
                    {
                        _rsfList = loadedData;
                    }
                }
                else
                {
                    // ファイルがない場合の初期データ
                    _rsfList.Add(new RsfItem { level = "C1s", rsf = 0.314 });
                    _rsfList.Add(new RsfItem { level = "O1s", rsf = 0.733 });
                    _rsfList.Add(new RsfItem { level = "Cu2p3", rsf = 2.626 });
                }

                // XAMLの DataGrid (x:Name="RsfDataGrid") にデータを渡す
                RsfDataGrid.ItemsSource = _rsfList;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"読み込みエラー: {ex.Message}");
            }
        }

        // --- 行追加ボタン (Add Row) ---
        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            // 空の行を追加
            _rsfList.Add(new RsfItem { level = "New", rsf = 0.0 });
        }

        // --- 行削除ボタン (Delete Row) ---
        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            // DataGridで選択されている行を取得して削除
            if (RsfDataGrid.SelectedItem is RsfItem selectedItem)
            {
                _rsfList.Remove(selectedItem);
            }
            else
            {
                MessageBox.Show("削除する行を選択してください。");
            }
        }

        // --- 保存ボタン (Save JSON) ---
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 見やすく整形してJSON化
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(_rsfList, options);

                // ファイルに書き込み
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

    // --- データ構造クラス (JSONの中身に対応) ---
    public class RsfItem
    {
        // プロパティ名は JSON のキーおよび XAML の Binding と一致させる
        public string level { get; set; } = "";
        public double rsf { get; set; } = 0.0;
    }
}