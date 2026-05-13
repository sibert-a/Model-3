using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DynamicModelingLab3
{
    class Program
    {
        static void Main()
        {
            Console.WriteLine("ДИНАМИЧЕСКОЕ МОДЕЛИРОВАНИЕ ПРОИЗВОДСТВЕННОЙ СИСТЕМЫ (ВАРИАНТ 1А)");
            var model = new ProductionModel();
            model.Run();
            Console.WriteLine("Моделирование завершено. Нажмите любую клавишу...");
            Console.ReadKey();
        }
    }

    public class ProductionModel
    {
        // ---------- Параметры из задания ----------
        private const double PA = 200;          // деталей А на изделие
        private const double PB = 400;          // деталей В на изделие

        private const double Dmin = 2;           // мин. задержка (всех звеньев)
        private const double Davg = 10;          // средняя задержка
        private const double Dmax = 18;          // макс. задержка
        private const double DstorageA = 10;      // задержка склада (аналог Dij)
        private const double DstorageB = 10;      

        private const double DeltaMuDop = 0.02;  // доп. отклонение μ
        private const double AlphaBase = 50.0;   // α = 50 * Δμ

        private const double ReplenishThreshold = 0.2;   // 20% от начального
        private const double StopThreshold = 0.05;       // 5% от начального
        private const double ReplenishPercent = 0.5;     // 50% от начального запаса (пополнение)

        private const double Smoothing = 0.2;            // сглаживание для Dij

        private const double DeltaT = 1.0;               // шаг
        private const double Tmax = 100.0;               // время моделирования

        private const double Y0A0 = 500;
        private const double Y0B0 = 500;
        private const double YijInit = 50;               // начальные уровни в звеньях
        private const double Yca0 = 25;
        private const double Ycb0 = 50;

        // ---------- Переменные модели ----------
        // Склады
        private double y0A, y0B;
        // Линия А (3 звена)
        private double y11, y12, y13;
        private double D11, D12, D13;
        private double X10, X11, X12, X13;
        // Линия В (2 звена)
        private double y21, y22;
        private double D21, D22;
        private double X20, X21, X22;
        // Сборка
        private double yCA, yCB;
        private double Z;          // количество выпущенных изделий
        private double Xsb;        // темп выпуска (0 или 1/Δt)

        private bool lineAStopped, lineBStopped;

        // История
        private List<double> timeHistory;
        private List<double> y0AHistory, y0BHistory;
        private List<double> yCAHistory, yCBHistory;
        private List<double> ZHistory;
        private List<double> D11History, D12History, D13History;
        private List<double> D21History, D22History;

        // новые метрики
        private List<double> y11History, y12History, y13History;
        private List<double> y21History, y22History;
        private List<double> X10History, X11History, X12History, X13History;
        private List<double> X20History, X21History, X22History;
        private List<double> XsbHistory;
        private List<double> muAHistory, muBHistory, deltaMuHistory, alphaHistory;
        private List<int> lineAStoppedHistory, lineBStoppedHistory;

        public ProductionModel()
        {
            InitHistory();
        }

        private void InitHistory()
        {
            timeHistory = new List<double>();
            y0AHistory = new List<double>();
            y0BHistory = new List<double>();
            yCAHistory = new List<double>();
            yCBHistory = new List<double>();
            ZHistory = new List<double>();
            D11History = new List<double>();
            D12History = new List<double>();
            D13History = new List<double>();
            D21History = new List<double>();
            D22History = new List<double>();

            // Новые:
            y11History = new List<double>();
            y12History = new List<double>();
            y13History = new List<double>();
            y21History = new List<double>();
            y22History = new List<double>();

            X10History = new List<double>();
            X11History = new List<double>();
            X12History = new List<double>();
            X13History = new List<double>();
            X20History = new List<double>();
            X21History = new List<double>();
            X22History = new List<double>();
            XsbHistory = new List<double>();

            muAHistory = new List<double>();
            muBHistory = new List<double>();
            deltaMuHistory = new List<double>();
            alphaHistory = new List<double>();

            lineAStoppedHistory = new List<int>();
            lineBStoppedHistory = new List<int>();
        }

        private void SetInitialConditions()
        {
            y0A = Y0A0;
            y0B = Y0B0;

            y11 = y12 = y13 = YijInit;
            y21 = y22 = YijInit;

            D11 = D12 = D13 = Davg;
            D21 = D22 = Davg;

            yCA = Yca0;
            yCB = Ycb0;
            Z = 0;
            Xsb = 0;

            // Начальные темпы (пересчитаются на первом шаге через UpdateFlowRates)
            X10 = y0A / DstorageA;
            X20 = y0B / DstorageB;
            X11 = y11 / D11;
            X12 = y12 / D12;
            X13 = y13 / D13;
            X21 = y21 / D21;
            X22 = y22 / D22;

            lineAStopped = false;
            lineBStopped = false;

            InitHistory();
        }

        // Вычисление коэффициентов μ
        private (double muA, double muB) ComputeMu()
        {
            return (yCA / PA, yCB / PB);
        }

        // Решение: выпуск изделия, если μA >= 1 и μB >= 1
        private void AssemblyDecision()
        {
            var (muA, muB) = ComputeMu();
            Xsb = (muA >= 1.0 && muB >= 1.0) ? 1.0 / DeltaT : 0.0;
        }

        // Расчёт задержек Dij по сглаженной формуле с управлением α
        private void UpdateDelays()
        {
            var (muA, muB) = ComputeMu();
            double deltaMu = muA - muB;
            double alpha = AlphaBase * deltaMu;
            alpha = Math.Clamp(alpha, -Dmax * 0.5, Dmax * 0.5);

            // Функция обновления одной задержки
            double ComputeNewDelay(double oldD, double y, double sign)
            {
                // Целевая задержка от текущего уровня (без скачков)
                double target = Dmin + Davg * (y / oldD);
                target = Math.Clamp(target, Dmin, Dmax);
                // Сглаженное изменение + управляющее воздействие
                double newD = oldD + Smoothing * (target - oldD) + sign * alpha;
                return Math.Clamp(newD, Dmin, Dmax);
            }

            if (!lineAStopped)
            {
                D11 = ComputeNewDelay(D11, y11, +1);
                D12 = ComputeNewDelay(D12, y12, +1);
                D13 = ComputeNewDelay(D13, y13, +1);
            }
            if (!lineBStopped)
            {
                // При deltaMu>0 (профицит А) линию В нужно ускорить (знак минус)
                double signB = (deltaMu > 0) ? -1 : +1;
                D21 = ComputeNewDelay(D21, y21, signB);
                D22 = ComputeNewDelay(D22, y22, signB);
            }
        }

        // Расчёт темпов Xij = yij / Dij, X10, X20 через задержку склада
        private void UpdateFlowRates()
        {
            if (!lineAStopped)
            {
                X11 = y11 / D11;
                X12 = y12 / D12;
                X13 = y13 / D13;
                // Темп со склада: уровень / задержка склада
                double desired = y0A / DstorageA;
                double maxPossible = y0A / DeltaT; // физическое ограничение
                X10 = Math.Min(desired, maxPossible);
            }
            else
            {
                X10 = X11 = X12 = X13 = 0;
            }

            if (!lineBStopped)
            {
                X21 = y21 / D21;
                X22 = y22 / D22;
                double desired = y0B / DstorageB;
                double maxPossible = y0B / DeltaT;
                X20 = Math.Min(desired, maxPossible);
            }
            else
            {
                X20 = X21 = X22 = 0;
            }
        }

        // Пополнение складов, остановка и возобновление линий
        private void ReplenishAndStop(double currentTime)
        {
            double replAmountA = ReplenishPercent * Y0A0; // 250
            double replAmountB = ReplenishPercent * Y0B0;
            double replThresholdA = ReplenishThreshold * Y0A0; // 100
            double replThresholdB = ReplenishThreshold * Y0B0;
            double stopThrA = StopThreshold * Y0A0; // 25
            double stopThrB = StopThreshold * Y0B0;

            // Пополнение (всегда, если запасы ниже порога)
            if (y0A < replThresholdA)
            {
                y0A += replAmountA;
                // Console.WriteLine($"  t={currentTime,5:F1}: Пополнение склада А +{replAmountA} → {y0A,6:F0}");
            }
            if (y0B < replThresholdB)
            {
                y0B += replAmountB;
                // Console.WriteLine($"  t={currentTime,5:F1}: Пополнение склада В +{replAmountB} → {y0B,6:F0}");
            }

            // Остановка / возобновление линии А
            if (!lineAStopped && y0A <= stopThrA)
            {
                lineAStopped = true;
                // Console.WriteLine($"  t={currentTime,5:F1}: ЛИНИЯ А ОСТАНОВЛЕНА");
            }
            else if (lineAStopped && y0A > stopThrA)
            {
                lineAStopped = false;
                // Console.WriteLine($"  t={currentTime,5:F1}: ЛИНИЯ А ВОЗОБНОВЛЕНА");
                // При возобновлении сбрасываем задержки на среднее значение
                D11 = D12 = D13 = Davg;
            }

            // Остановка / возобновление линии В
            if (!lineBStopped && y0B <= stopThrB)
            {
                lineBStopped = true;
                // Console.WriteLine($"  t={currentTime,5:F1}: ЛИНИЯ В ОСТАНОВЛЕНА");
            }
            else if (lineBStopped && y0B > stopThrB)
            {
                lineBStopped = false;
                // Console.WriteLine($"  t={currentTime,5:F1}: ЛИНИЯ В ВОЗОБНОВЛЕНА");
                D21 = D22 = Davg;
            }
        }

        // Обновление уровней (с использованием текущих темпов)
        private void UpdateLevels()
        {
            // Склады: пополнение уже учтено в ReplenishAndStop, здесь только отток
            y0A = Math.Max(y0A + DeltaT * (0 - X10), 0);
            y0B = Math.Max(y0B + DeltaT * (0 - X20), 0);

            // Линия А
            y11 = Math.Max(y11 + DeltaT * (X10 - X11), 0);
            y12 = Math.Max(y12 + DeltaT * (X11 - X12), 0);
            y13 = Math.Max(y13 + DeltaT * (X12 - X13), 0);

            // Линия В
            y21 = Math.Max(y21 + DeltaT * (X20 - X21), 0);
            y22 = Math.Max(y22 + DeltaT * (X21 - X22), 0);

            // Сборка
            yCA = Math.Max(yCA + DeltaT * (X13 - PA * Xsb), 0);
            yCB = Math.Max(yCB + DeltaT * (X22 - PB * Xsb), 0);
            Z += DeltaT * Xsb;   // количество выпущенных изделий
        }

        // Сохранение данных в историю
        private void SaveToHistory(double t)
        {
            timeHistory.Add(t);
            y0AHistory.Add(y0A);
            y0BHistory.Add(y0B);
            yCAHistory.Add(yCA);
            yCBHistory.Add(yCB);
            ZHistory.Add(Z);
            D11History.Add(D11);
            D12History.Add(D12);
            D13History.Add(D13);
            D21History.Add(D21);
            D22History.Add(D22);

            // Новые:
            y11History.Add(y11);
            y12History.Add(y12);
            y13History.Add(y13);
            y21History.Add(y21);
            y22History.Add(y22);

            X10History.Add(X10);
            X11History.Add(X11);
            X12History.Add(X12);
            X13History.Add(X13);
            X20History.Add(X20);
            X21History.Add(X21);
            X22History.Add(X22);
            XsbHistory.Add(Xsb);

            var (muA, muB) = ComputeMu();
            double deltaMu = muA - muB;
            double alpha = AlphaBase * deltaMu;
            muAHistory.Add(muA);
            muBHistory.Add(muB);
            deltaMuHistory.Add(deltaMu);
            alphaHistory.Add(alpha);

            lineAStoppedHistory.Add(lineAStopped ? 1 : 0);
            lineBStoppedHistory.Add(lineBStopped ? 1 : 0);
        }

        private void PrintHeader()
        {
            Console.WriteLine("┌───────┬──────────┬──────────┬──────────┬──────────┬───────┬──────────┬──────────┬──────────┬──────────┬──────────┐");
            Console.WriteLine("│   t   │   y0A    │   y0B    │   yCA    │   yCB    │   Z   │   D11    │   D12    │   D13    │   D21    │   D22    │");
            Console.WriteLine("├───────┼──────────┼──────────┼──────────┼──────────┼───────┼──────────┼──────────┼──────────┼──────────┼──────────┤");
        }

        private void PrintState(double t)
        {
            Console.WriteLine($"│ {t,5:F1} │ {y0A,8:F1} │ {y0B,8:F1} │ {yCA,8:F1} │ {yCB,8:F1} │ {Z,5:F0} │ {D11,8:F2} │ {D12,8:F2} │ {D13,8:F2} │ {D21,8:F2} │ {D22,8:F2} │");
        }

        private void PrintFooter()
        {
            Console.WriteLine("└───────┴──────────┴──────────┴──────────┴──────────┴───────┴──────────┴──────────┴──────────┴──────────┴──────────┘");
        }

        private void ExportToCsv()
        {
            using var sw = new StreamWriter("dynamic_model_results.csv");
            // Заголовок (все метрики)
            sw.WriteLine("t;y0A;y0B;y11;y12;y13;y21;y22;yCA;yCB;Z;" +
                         "D11;D12;D13;D21;D22;" +
                         "X10;X11;X12;X13;X20;X21;X22;Xsb;" +
                         "muA;muB;deltaMu;alpha;" +
                         "lineAStopped;lineBStopped");

            for (int i = 0; i < timeHistory.Count; i++)
            {
                sw.WriteLine($"{timeHistory[i]};" +
                             $"{y0AHistory[i]};{y0BHistory[i]};" +
                             $"{y11History[i]};{y12History[i]};{y13History[i]};{y21History[i]};{y22History[i]};" +
                             $"{yCAHistory[i]};{yCBHistory[i]};{ZHistory[i]};" +
                             $"{D11History[i]};{D12History[i]};{D13History[i]};{D21History[i]};{D22History[i]};" +
                             $"{X10History[i]};{X11History[i]};{X12History[i]};{X13History[i]};{X20History[i]};{X21History[i]};{X22History[i]};{XsbHistory[i]};" +
                             $"{muAHistory[i]};{muBHistory[i]};{deltaMuHistory[i]};{alphaHistory[i]};" +
                             $"{lineAStoppedHistory[i]};{lineBStoppedHistory[i]}");
            }
            Console.WriteLine("Результаты (все метрики) сохранены в dynamic_model_results.csv");
        }

        public void Run()
        {
            Console.WriteLine("\nПАРАМЕТРЫ МОДЕЛИ:");
            Console.WriteLine($"  ПА={PA}, ПВ={PB}; Dmin={Dmin}, Dср={Davg}, Dmax={Dmax}; Δμдоп={DeltaMuDop}; α = {AlphaBase}·Δμ");
            Console.WriteLine($"  DstorageA={DstorageA}; DstorageB={DstorageB}; пополнение: +{ReplenishPercent * 100}% от начального (={ReplenishPercent * Y0A0}) при запасе < {ReplenishThreshold * 100}%;");
            Console.WriteLine($"  остановка при ≤ {StopThreshold * 100}%, возобновление при превышении; сглаживание Dij = {Smoothing}");
            Console.WriteLine($"  Δt={DeltaT}, T={Tmax}\n");

            SetInitialConditions();
            PrintHeader();

            SaveToHistory(0);
            PrintState(0);

            for (int step = 1; step <= Tmax / DeltaT; step++)
            {
                double t = step * DeltaT;

                AssemblyDecision();      // 1. Выпуск изделия
                UpdateDelays();          // 2. Обновление задержек (формула (6) + сглаживание)
                UpdateFlowRates();       // 3. Расчёт темпов
                ReplenishAndStop(t);     // 4. Пополнение и проверка остановки/возобновления
                UpdateLevels();          // 5. Обновление уровней

                SaveToHistory(t);

                if (step % 5 == 0 || step == Tmax / DeltaT)
                    PrintState(t);
            }

            PrintFooter();

            Console.WriteLine("\nИТОГОВЫЕ РЕЗУЛЬТАТЫ:");
            Console.WriteLine($"  Выпущено изделий С: {Z:F0} шт.");
            Console.WriteLine($"  Остаток на складе А: {y0A:F2}, В: {y0B:F2}");
            Console.WriteLine($"  Остаток в сборке А: {yCA:F2}, В: {yCB:F2}");
            if (lineAStopped) Console.WriteLine("  Линия А была остановлена (но могла возобновиться)");
            if (lineBStopped) Console.WriteLine("  Линия В была остановлена (но могла возобновиться)");
            Console.WriteLine($"  Средний уровень А в сборке: {yCAHistory.Average():F2}");
            Console.WriteLine($"  Средний уровень В в сборке: {yCBHistory.Average():F2}");

            ExportToCsv();
        }
    }
}