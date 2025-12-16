import numpy as np
from scipy.optimize import curve_fit

# --- 1. フィッティング用関数定義 (Pseudo-Voigt) ---
def pseudo_voigt(x, amp, center, fwhm, mix_ratio):
    """
    Pseudo-Voigt関数 (ガウス関数とローレンツ関数の線形結合)
    mix_ratio: 0=Gaussian, 1=Lorentzian
    """
    # 半値幅(FWHM)から各パラメータへの変換
    sigma = fwhm / (2 * np.sqrt(2 * np.log(2))) # ガウス用
    gamma = fwhm / 2.0                          # ローレンツ用

    # ガウス関数
    g = np.exp(-((x - center)**2) / (2 * sigma**2))
    
    # ローレンツ関数
    l = 1 / (1 + ((x - center) / gamma)**2)
    
    # 混合
    return amp * ((1 - mix_ratio) * g + mix_ratio * l)

# --- 2. 複数のピークを足し合わせるモデル関数 ---
def multi_peak_model(x, *params):
    """
    curve_fitに渡すための、全ピークの合計を返す関数
    """
    y_sum = np.zeros_like(x)
    num_peaks = len(params) // 4
    
    for i in range(num_peaks):
        amp = params[i*4]
        cen = params[i*4+1]
        fwhm = params[i*4+2]
        mix = params[i*4+3]
        y_sum += pseudo_voigt(x, amp, cen, fwhm, mix)
        
    return y_sum

# --- 3. メインのフィッティング実行関数 ---
def perform_fitting(x, y, config, verbose=False):
    """
    指定された範囲のデータ(x, y)に対し、configの設定に基づいてピーク分離を行う
    エラーが起きても停止せず、Noneを返して処理を継続させる
    """
    # 1. 初期パラメータ作成
    try:
        initial_guess = []
        bounds_min = []
        bounds_max = []
        peak_infos = []

        # xの範囲など事前チェック
        if len(x) < 5 or len(y) < 5:
            return None, None

        for peak_conf in config:
            # パラメータ読み込み (name, position, fwhm, etc...)
            # ※ configの構造に合わせてキー名は適宜調整してください
            p_name = peak_conf["name"]
            
            # 位置 (center)
            p_pos = float(peak_conf["position"])
            p_pos_min = p_pos - 0.5 # ±0.5eV程度動けるとする
            p_pos_max = p_pos + 0.5
            
            # FWHM (半値幅)
            p_fwhm = float(peak_conf["fwhm"])
            p_fwhm_min = p_fwhm * 0.5
            p_fwhm_max = p_fwhm * 1.5
            
            # 強度 (Amplitude) - 初期値は適当、範囲は0〜無限大
            # 簡易的にyの最大値を参考にしても良い
            p_amp = np.max(y) * 0.5
            p_amp_min = 0.0
            p_amp_max = np.inf

            # 混合比 (Gaussian/Lorentzian mix)
            # 0=Gauss, 1=Lorentz
            p_mix = 0.3
            p_mix_min = 0.0
            p_mix_max = 1.0

            # パラメータ追加順序: Amp, Center, FWHM, Mix
            initial_guess.extend([p_amp, p_pos, p_fwhm, p_mix])
            
            bounds_min.extend([p_amp_min, p_pos_min, p_fwhm_min, p_mix_min])
            bounds_max.extend([p_amp_max, p_pos_max, p_fwhm_max, p_mix_max])
            
            peak_infos.append(peak_conf)

        # 境界条件リスト作成
        bounds_list = (bounds_min, bounds_max)

    except Exception as e:
        if verbose: print(f"Config Error: {e}")
        return None, None

    # 2. フィッティング実行 (ここが一番落ちやすいのでガードする)
    try:
        popt, pcov = curve_fit(
            multi_peak_model, 
            x, 
            y, 
            p0=initial_guess, 
            bounds=bounds_list, 
            maxfev=10000 # 試行回数を少し増やす
        )
    except Exception as e:
        # RuntimeError (収束せず) や OptimizeWarning (共分散なし) など
        if verbose: print(f"Fitting Failed: {e}")
        return None, None

    # 3. 結果整理 (Excel出力用に構造を変えない)
    try:
        fitted_peaks = []
        num_peaks = len(peak_infos)
        
        # 全体のフィットカーブを計算
        y_sum_fit = multi_peak_model(x, *popt)
        
        for i in range(num_peaks):
            amp, cen, fwhm, mix = popt[i*4 : (i+1)*4]
            
            # このピーク単体の波形
            y_comp = pseudo_voigt(x, amp, cen, fwhm, mix)
            
            # 面積計算 (台形積分)
            area = np.trapz(y_comp, x) 
            if area < 0: area = 0 # 念のため
            
            fitted_peaks.append({
                "name": peak_infos[i]["name"],
                "amplitude": amp,
                "center": cen,
                "fwhm": fwhm,
                "mix_ratio": mix,
                "y_data": y_comp, # 個別波形データ
                "area": area,
                # Excel出力に必要な 'ratio' は後で全体の合計面積から計算して入れるのが一般的ですが、
                # ここでは暫定的に入れておき、呼び出し元で再計算しても良いです
                "ratio": 0.0 
            })
            
        # 面積比 (Ratio) の再計算
        total_area = sum(p["area"] for p in fitted_peaks)
        if total_area > 0:
            for p in fitted_peaks:
                p["ratio"] = (p["area"] / total_area) * 100.0

        return fitted_peaks, y_sum_fit

    except Exception as e:
        if verbose: print(f"Result Processing Error: {e}")
        return None, None