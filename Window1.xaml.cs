using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using System;
using System.IO;            // ファイル操作に必要
using System.Text.Json;     // JSON操作に必要
using System.Windows;       // WPFの基本機能

namespace XPSUI
{
    public partial class Window1 : Window
    {
        // 保存するファイル名を指定
        private const string JsonFileName = "shift_setting.json";

        public Window1()
        {
            InitializeComponent();

            // 画面が開いたときに設定を読み込む
            LoadSettings();
        }

        /// <summary>
        /// 保存先のフルパスを取得するメソッド
        /// 場所: C:\Users\[ユーザー名]\Documents\XPSUI_setting\shift_setting.json
        /// </summary>
        private string GetShiftSettings()
        {
            // 1. マイドキュメントの場所を取得
            string myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            // 2. フォルダ名を "XPSUI_setting" に設定
            string folderPath = System.IO.Path.Combine(myDocuments, "XPSUI_setting");

            // 3. フォルダが存在しない場合は作成する
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            // 4. ファイル名を結合して返す
            return System.IO.Path.Combine(folderPath, JsonFileName);
        }

        // Save Settings ボタンが押されたときの処理
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 画面の入力値を数値に変換 (変換できない場合は0.0になる)
                double.TryParse(Shift_Peak_Center.Text, out double shiftCenter);
                double.TryParse(X_Max.Text, out double xMax);
                double.TryParse(X_min.Text, out double xMin);

                // 保存用クラスにデータをセット
                var settings = new AppSettings
                {
                    ShiftPeakCenter = shiftCenter,
                    XMax = xMax,
                    XMin = xMin
                };

                // 見やすい形式でJSON文字列に変換
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(settings, options);

                // ファイルに書き込み
                string fullPath = GetShiftSettings();
                File.WriteAllText(fullPath, jsonString);

                MessageBox.Show($"設定を保存しました。\n場所: {fullPath}", "保存完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 設定ファイルを読み込む処理
        private void LoadSettings()
        {
            try
            {
                string fullPath = GetShiftSettings();

                // ファイルが存在する場合のみ読み込む
                if (File.Exists(fullPath))
                {
                    string jsonString = File.ReadAllText(fullPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(jsonString);

                    if (settings != null)
                    {
                        // 読み込んだ値を画面のTextBoxに反映
                        Shift_Peak_Center.Text = settings.ShiftPeakCenter.ToString();
                        X_Max.Text = settings.XMax.ToString();
                        X_min.Text = settings.XMin.ToString();
                    }
                }
            }
            catch
            {
                // 読み込みエラー時は何もしない（デフォルト値のまま）
            }
        }
    }

    // JSONデータの構造定義クラス
    public class AppSettings
    {
        // 初期値（ファイルがない時に使われる値）
        public double ShiftPeakCenter { get; set; } = 284.4;
        public double XMax { get; set; } = 290.0;
        public double XMin { get; set; } = 280.0;
    }
}