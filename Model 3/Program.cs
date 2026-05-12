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

        private const double DeltaMuDop = 0.02;  // доп. отклонение μ
        private const double AlphaBase = 50.0;   // α = 50 * Δμ

        private const double ReplenishThreshold = 0.2;   // 20% от начального
        private const double StopThreshold = 0.05;       // 5% от начального
        private const double ReplenishAmount = 200;      // мгновенное пополнение

        private const double DesiredInputRate = 10.0;    // желаемый темп со склада в линию

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

            X10 = X20 = DesiredInputRate;      // начальный темп со склада
            X11 = X12 = X13 = DesiredInputRate;
            X21 = X22 = DesiredInputRate;

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

        // Расчёт задержек Dij по формуле (6) с α = AlphaBase * Δμ
        private void UpdateDelays()
        {
            var (muA, muB) = ComputeMu();
            double deltaMu = muA - muB;

            // Если линии остановлены – не меняем задержки
            if (lineAStopped && lineBStopped) return;

            double alpha = AlphaBase * deltaMu;   // может быть отрицательным
            // Ограничим α, чтобы изменения не выходили за пределы Dmin/Dmax слишком резко
            alpha = Math.Clamp(alpha, -Dmax * 0.5, Dmax * 0.5);

            // Для линии А (3 звена)
            if (!lineAStopped)
            {
                D11 = Dmin + Davg * (y11 / D11) + alpha;
                D12 = Dmin + Davg * (y12 / D12) + alpha;
                D13 = Dmin + Davg * (y13 / D13) + alpha;
                // Ограничение и защита от деления на ноль
                D11 = Math.Clamp(D11, Dmin, Dmax);
                D12 = Math.Clamp(D12, Dmin, Dmax);
                D13 = Math.Clamp(D13, Dmin, Dmax);
            }

            // Для линии В (2 звена) – знак α противоположный (ускорение/замедление)
            if (!lineBStopped)
            {
                // При deltaMu>0 (А переполнена) – линию В нужно ускорить => уменьшить D (знак минус)
                double alphaB = (deltaMu > 0) ? -alpha : alpha;
                D21 = Dmin + Davg * (y21 / D21) + alphaB;
                D22 = Dmin + Davg * (y22 / D22) + alphaB;
                D21 = Math.Clamp(D21, Dmin, Dmax);
                D22 = Math.Clamp(D22, Dmin, Dmax);
            }
        }

        // Расчёт темпов Xij = yij / Dij
        private void UpdateFlowRates()
        {
            if (!lineAStopped)
            {
                X11 = y11 / D11;
                X12 = y12 / D12;
                X13 = y13 / D13;
                // Входной темп со склада: желаемый, но ограничен наличием заготовок
                double maxPossible = y0A / DeltaT;
                X10 = Math.Min(DesiredInputRate, maxPossible);
            }
            else
            {
                X10 = X11 = X12 = X13 = 0;
            }

            if (!lineBStopped)
            {
                X21 = y21 / D21;
                X22 = y22 / D22;
                double maxPossible = y0B / DeltaT;
                X20 = Math.Min(DesiredInputRate, maxPossible);
            }
            else
            {
                X20 = X21 = X22 = 0;
            }
        }

        private void ReplenishAndStop(double currentTime)
        {
            double replThresholdA = ReplenishThreshold * Y0A0;
            double replThresholdB = ReplenishThreshold * Y0B0;
            double stopThrA = StopThreshold * Y0A0;
            double stopThrB = StopThreshold * Y0B0;

            if (y0A < replThresholdA)
            {
                y0A += ReplenishAmount;
                //Console.WriteLine($"  t={currentTime,5:F1}: Пополнение склада А +{ReplenishAmount} → {y0A,6:F0}");
            }
            if (y0B < replThresholdB)
            {
                y0B += ReplenishAmount;
                //Console.WriteLine($"  t={currentTime,5:F1}: Пополнение склада В +{ReplenishAmount} → {y0B,6:F0}");
            }

            // Остановка линии, если запас упал до 5% или ниже
            if (!lineAStopped && y0A <= stopThrA)
            {
                lineAStopped = true;
                Console.WriteLine($"  t={currentTime,5:F1}: ЛИНИЯ А ОСТАНОВЛЕНА (запас {y0A,6:F0} ≤ {stopThrA})");
            }
            // Возобновление, если запас поднялся строго выше 5%
            else if (lineAStopped && y0A > stopThrA)
            {
                lineAStopped = false;
                Console.WriteLine($"  t={currentTime,5:F1}: ЛИНИЯ А ВОЗОБНОВЛЕНА (запас {y0A,6:F0} > {stopThrA})");
            }

            if (!lineBStopped && y0B <= stopThrB)
            {
                lineBStopped = true;
                Console.WriteLine($"  t={currentTime,5:F1}: ЛИНИЯ В ОСТАНОВЛЕНА (запас {y0B,6:F0} ≤ {stopThrB})");
            }
            else if (lineBStopped && y0B > stopThrB)
            {
                lineBStopped = false;
                Console.WriteLine($"  t={currentTime,5:F1}: ЛИНИЯ В ВОЗОБНОВЛЕНА (запас {y0B,6:F0} > {stopThrB})");
            }
        }

        // Обновление уровней (с использованием текущих темпов)
        private void UpdateLevels()
        {
            // Склады: пополнение происходит в ReplenishAndStop, здесь только отток
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
            sw.WriteLine("t;y0A;y0B;yCA;yCB;Z;D11;D12;D13;D21;D22");
            for (int i = 0; i < timeHistory.Count; i++)
                sw.WriteLine($"{timeHistory[i]};{y0AHistory[i]};{y0BHistory[i]};{yCAHistory[i]};{yCBHistory[i]};{ZHistory[i]};{D11History[i]};{D12History[i]};{D13History[i]};{D21History[i]};{D22History[i]}");
            Console.WriteLine("Результаты сохранены в dynamic_model_results.csv");
        }

        public void Run()
        {
            Console.WriteLine("\nПАРАМЕТРЫ МОДЕЛИ:");
            Console.WriteLine($"  ПА={PA}, ПВ={PB}; Dmin={Dmin}, Dср={Davg}, Dmax={Dmax}; дельта-мю_доп={DeltaMuDop}; альфа = {AlphaBase}·дельта-мю");
            Console.WriteLine($"  Пополнение: +{ReplenishAmount} при запасе < {ReplenishThreshold * 100}%; остановка при <= {StopThreshold * 100}%");
            Console.WriteLine($"  Δt={DeltaT}, T={Tmax}, желаемый входной темп = {DesiredInputRate}\n");

            SetInitialConditions();
            PrintHeader();

            // Сохраняем начальное состояние
            SaveToHistory(0);
            PrintState(0);

            for (int step = 1; step <= Tmax / DeltaT; step++)
            {
                double t = step * DeltaT;

                // 1. Решение о выпуске
                AssemblyDecision();

                // 2. Расчёт новых задержек (по формуле (6) с учётом Δμ)
                UpdateDelays();

                // 3. Расчёт темпов
                UpdateFlowRates();

                // 4. Пополнение и остановка (до обновления уровней, чтобы избежать отрицательных запасов)
                ReplenishAndStop(t);

                // 5. Обновление уровней
                UpdateLevels();

                // Сохраняем результаты
                SaveToHistory(t);

                if (step % 5 == 0 || step == Tmax / DeltaT)
                    PrintState(t);
            }

            PrintFooter();

            Console.WriteLine("\nИТОГОВЫЕ РЕЗУЛЬТАТЫ:");
            Console.WriteLine($"  Выпущено изделий С: {Z:F0} шт.");
            Console.WriteLine($"  Остаток на складе А: {y0A:F2}, В: {y0B:F2}");
            Console.WriteLine($"  Остаток в сборке А: {yCA:F2}, В: {yCB:F2}");
            if (lineAStopped) Console.WriteLine("  Линия А была остановлена.");
            if (lineBStopped) Console.WriteLine("  Линия В была остановлена.");

            ExportToCsv();
        }
    }
}