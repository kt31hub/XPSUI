using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Python.Runtime;

namespace XPSUI
{
    public class XpsEngine
    {
        // --- データ保持エリア ---
        public List<string> Tags { get; private set; } = new List<string>();
        public List<double[]> XData { get; private set; } = new List<double[]>();
        public List<double[]> YData { get; private set; } = new List<double[]>();

        // 設定ファイル名
        private const string PathConfigFileName = "path.json";
        private const string ShiftSettingFileName = "shift_setting.json";

        // ---------------------------------------------------------
        // 1. Python初期化・設定関連
        // ---------------------------------------------------------
        public bool TryInitializePython(bool silent)
        {
            if (PythonEngine.IsInitialized) return true;

            try
            {
                string dllPath = LoadPythonPath();
                if (!string.IsNullOrEmpty(dllPath) && File.Exists(dllPath))
                {
                    Runtime.PythonDLL = dllPath;
                }

                PythonEngine.Initialize();
                PythonEngine.BeginAllowThreads();
                return true;
            }
            catch (Exception ex)
            {
                if (!silent) throw ex; // 手動時はエラーを上に投げる
                return false;
            }
        }

        public void SavePythonPath(string path)
        {
            var data = new PathConfig { PythonDllPath = path };
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetConfigPath(PathConfigFileName), json);
        }

        private string LoadPythonPath()
        {
            try
            {
                string path = GetConfigPath(PathConfigFileName);
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var data = JsonSerializer.Deserialize<PathConfig>(json);
                    return data?.PythonDllPath;
                }
            }
            catch { }
            return null;
        }

        // ---------------------------------------------------------
        // 2. データ読み込み (Python連携)
        // ---------------------------------------------------------
        public void LoadData(string filePath)
        {
            using (Py.GIL())
            {
                dynamic sys = Py.Import("sys");
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;

                // Pythonパス設定
                sys.path.append(exeDir);
                sys.path.append(Path.Combine(exeDir, "python")); // pythonフォルダ

                dynamic xpsasc = Py.Import("XPSASC");
                dynamic result = xpsasc.load_allspe(filePath);

                var pyTags = result[0];
                var pyX = result[1];
                var pyY = result[2];

                // データを一新
                Tags.Clear();
                XData.Clear();
                YData.Clear();

                long count = pyTags.__len__();
                for (int i = 0; i < count; i++)
                {
                    Tags.Add(pyTags[i].ToString());
                    XData.Add((double[])pyX[i].As<double[]>());
                    YData.Add((double[])pyY[i].As<double[]>());
                }
            }
        }

        // ---------------------------------------------------------
        // 3. 帯電補正 (Python連携)
        // ---------------------------------------------------------
        public AppSettings LoadShiftSettings()
        {
            string path = GetConfigPath(ShiftSettingFileName);
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                catch { }
            }
            return new AppSettings();
        }

        public void ApplyShift(AppSettings settings)
        {
            using (Py.GIL())
            {
                dynamic sys = Py.Import("sys");
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                sys.path.append(exeDir);
                sys.path.append(Path.Combine(exeDir, "python"));

                dynamic xpscal = Py.Import("XPSCAL");

                dynamic result = xpscal.shift(
                    Tags, XData, YData,
                    settings.XMin, settings.XMax, settings.ShiftPeakCenter
                );

                var pyX = result[0];
                var pyY = result[1];

                // データを更新
                XData.Clear();
                YData.Clear();

                long count = pyX.__len__();
                for (int i = 0; i < count; i++)
                {
                    XData.Add((double[])pyX[i].As<double[]>());
                    YData.Add((double[])pyY[i].As<double[]>());
                }
            }
        }

        // ---------------------------------------------------------
        // ヘルパー
        // ---------------------------------------------------------
        private string GetConfigPath(string fileName)
        {
            string myDoc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string folder = Path.Combine(myDoc, "XPSUI_setting");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            return Path.Combine(folder, fileName);
        }

        // 内部クラス
        private class PathConfig { public string PythonDllPath { get; set; } = ""; }
    }
}