using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DynamicModelingLab3
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("              ДИНАМИЧЕСКОЕ МОДЕЛИРОВАНИЕ ПРОИЗВОДСТВЕННОЙ СИСТЕМЫ");

            var model = new ProductionModel();
            model.Run();

            Console.WriteLine("Моделирование завершено. Нажмите любую клавишу для выхода...");
            Console.ReadKey();
        }
    }

    /// <summary>
    /// Класс модели производственной системы (трёхзвенная структура)
    /// </summary>
    public class ProductionModel
    {
        // ==================== ПАРАМЕТРЫ СИСТЕМЫ ====================
        private const double PA = 200;      // Деталей А на изделие
        private const double PB = 400;      // Деталей В на изделие

        private const double Dmin = 2;      // Минимальная задержка
        private const double Davg = 10;     // Средняя задержка
        private const double Dmax = 18;     // Максимальная задержка

        private const double D1 = 5;        // Задержка в 1-м звене
        private const double D3 = 5;        // Задержка в 3-м звене

        private const double DeltaMuDop = 0.02;   // Допустимая разница
        private const double DCoefBase = 50;     // d = 50/|Δμ|

        private const double Gamma = 0.2;         // 20% для пополнения
        private const double Epsilon = 0.05;      // 5% для остановки

        private const double Q_A = 200;            // Пополнение склада А
        private const double Q_B = 200;            // Пополнение склада В

        private const double DeltaT = 1;           // Шаг моделирования
        private const double Tmax = 100;           // Время моделирования

        private const double Y0A0 = 500;
        private const double Y0B0 = 500;
        private const double YInit = 50;
        private const double YCA0 = 25;
        private const double YCB0 = 50;
        private const double XInit = 10;

        // ==================== ПЕРЕМЕННЫЕ МОДЕЛИ ====================
        private double y0A, y0B;
        private double y11, y12, y13;
        private double y21, y22, y23;
        private double yCA, yCB;
        private double Z;
        private double X11, X12, X13;
        private double X21, X22, X23;
        private double Xsb;
        private double D12, D22;
        private bool lineAStopped, lineBStopped;

        // История
        private List<double> timeHistory;
        private List<double> y0AHistory, y0BHistory;
        private List<double> yCAHistory, yCBHistory;
        private List<double> ZHistory;
        private List<double> D12History, D22History;

        public ProductionModel()
        {
            InitializeHistory();
        }

        private void InitializeHistory()
        {
            timeHistory = new List<double>();
            y0AHistory = new List<double>();
            y0BHistory = new List<double>();
            yCAHistory = new List<double>();
            yCBHistory = new List<double>();
            ZHistory = new List<double>();
            D12History = new List<double>();
            D22History = new List<double>();
        }

        private void SetInitialConditions()
        {
            y0A = Y0A0;
            y0B = Y0B0;
            y11 = YInit;
            y12 = YInit;
            y13 = YInit;
            y21 = YInit;
            y22 = YInit;
            y23 = YInit;
            yCA = YCA0;
            yCB = YCB0;
            Z = 0;
            X11 = XInit;
            X12 = XInit;
            X13 = XInit;
            X21 = XInit;
            X22 = XInit;
            X23 = XInit;
            Xsb = 0;
            D12 = Davg;
            D22 = Davg;
            lineAStopped = false;
            lineBStopped = false;

            InitializeHistory();
        }

        private void CalculateMu(out double muA, out double muB)
        {
            muA = yCA / PA;
            muB = yCB / PB;
        }

        private void CalculateAssemblyRate()
        {
            CalculateMu(out double muA, out double muB);
            Xsb = (muA >= 1.0 && muB >= 1.0) ? 1.0 / DeltaT : 0;
        }

        private void CalculateDelays(double prevD12, double prevD22)
        {
            CalculateMu(out double muA, out double muB);
            double deltaMu = muA - muB;

            if (lineAStopped && lineBStopped)
                return;

            if (Math.Abs(deltaMu) > DeltaMuDop)
            {
                double dCoef = DCoefBase / Math.Abs(deltaMu);
                dCoef = Math.Min(dCoef, 10.0);
                dCoef = Math.Max(dCoef, 0.1);
                double adjustment = dCoef * Dmax;

                if (deltaMu > 0)
                {
                    if (!lineAStopped)
                    {
                        D12 = Dmin + Davg * (y12 / prevD12) + adjustment;
                        D12 = Math.Min(D12, Dmax);
                        D12 = Math.Max(D12, Dmin);
                    }
                    if (!lineBStopped)
                    {
                        D22 = Dmin + Davg * (y22 / prevD22) - adjustment;
                        D22 = Math.Min(D22, Dmax);
                        D22 = Math.Max(D22, Dmin);
                    }
                }
                else
                {
                    if (!lineAStopped)
                    {
                        D12 = Dmin + Davg * (y12 / prevD12) - adjustment;
                        D12 = Math.Min(D12, Dmax);
                        D12 = Math.Max(D12, Dmin);
                    }
                    if (!lineBStopped)
                    {
                        D22 = Dmin + Davg * (y22 / prevD22) + adjustment;
                        D22 = Math.Min(D22, Dmax);
                        D22 = Math.Max(D22, Dmin);
                    }
                }
            }
            else
            {
                if (!lineAStopped)
                {
                    D12 = D12 + 0.1 * (Davg - D12);
                    D12 = Math.Min(D12, Dmax);
                    D12 = Math.Max(D12, Dmin);
                }
                if (!lineBStopped)
                {
                    D22 = D22 + 0.1 * (Davg - D22);
                    D22 = Math.Min(D22, Dmax);
                    D22 = Math.Max(D22, Dmin);
                }
            }
        }

        private void CalculateFlowRates()
        {
            if (!lineAStopped)
            {
                X11 = y11 / D1;
                X12 = y12 / D12;
                X13 = y13 / D3;
            }
            else
            {
                X11 = 0; X12 = 0; X13 = 0;
            }

            if (!lineBStopped)
            {
                X21 = y21 / D1;
                X22 = y22 / D22;
                X23 = y23 / D3;
            }
            else
            {
                X21 = 0; X22 = 0; X23 = 0;
            }
        }

        private void CheckReplenishmentAndStop(double currentTime)
        {
            double replenishThreshold = Gamma * Y0A0;
            double stopThreshold = Epsilon * Y0A0;

            if (y0A < replenishThreshold && !lineAStopped)
            {
                y0A += Q_A;
                Console.WriteLine($"  СОБЫТИЕ t={currentTime:F1} | Пополнение склада А +{Q_A} | Стало: {y0A:F0}");
            }
            if (y0B < replenishThreshold && !lineBStopped)
            {
                y0B += Q_B;
                Console.WriteLine($"  СОБЫТИЕ t={currentTime:F1} | Пополнение склада В +{Q_B} | Стало: {y0B:F0}");
            }

            if (!lineAStopped && y0A <= stopThreshold)
            {
                lineAStopped = true;
                Console.WriteLine($"  СОБЫТИЕ t={currentTime:F1} | ЛИНИЯ А ОСТАНОВЛЕНА | Запас: {y0A:F0} ≤ {stopThreshold}");
            }
            if (!lineBStopped && y0B <= stopThreshold)
            {
                lineBStopped = true;
                Console.WriteLine($"  СОБЫТИЕ t={currentTime:F1} | ЛИНИЯ В ОСТАНОВЛЕНА | Запас: {y0B:F0} ≤ {stopThreshold}");
            }
        }

        private void UpdateLevels()
        {
            double cur_y0A = y0A, cur_y0B = y0B;
            double cur_y11 = y11, cur_y12 = y12, cur_y13 = y13;
            double cur_y21 = y21, cur_y22 = y22, cur_y23 = y23;
            double cur_yCA = yCA, cur_yCB = yCB;
            double cur_Z = Z;
            double cur_X11 = X11, cur_X12 = X12, cur_X13 = X13;
            double cur_X21 = X21, cur_X22 = X22, cur_X23 = X23;
            double cur_Xsb = Xsb;

            y0A = Math.Max(cur_y0A + DeltaT * (0 - cur_X11), 0);
            y0B = Math.Max(cur_y0B + DeltaT * (0 - cur_X21), 0);

            y11 = Math.Max(cur_y11 + DeltaT * (cur_X11 - cur_X11), 0);
            y12 = Math.Max(cur_y12 + DeltaT * (cur_X11 - cur_X12), 0);
            y13 = Math.Max(cur_y13 + DeltaT * (cur_X12 - cur_X13), 0);

            y21 = Math.Max(cur_y21 + DeltaT * (cur_X21 - cur_X21), 0);
            y22 = Math.Max(cur_y22 + DeltaT * (cur_X21 - cur_X22), 0);
            y23 = Math.Max(cur_y23 + DeltaT * (cur_X22 - cur_X23), 0);

            yCA = Math.Max(cur_yCA + DeltaT * (cur_X13 - PA * cur_Xsb), 0);
            yCB = Math.Max(cur_yCB + DeltaT * (cur_X23 - PB * cur_Xsb), 0);
            Z = cur_Z + DeltaT * cur_Xsb;
        }

        private void SaveToHistory(double time)
        {
            timeHistory.Add(time);
            y0AHistory.Add(y0A);
            y0BHistory.Add(y0B);
            yCAHistory.Add(yCA);
            yCBHistory.Add(yCB);
            ZHistory.Add(Z);
            D12History.Add(D12);
            D22History.Add(D22);
        }

        private void PrintState(double time)
        {
            Console.WriteLine($"│ {time,6:F1} │ {y0A,9:F1} │ {y0B,9:F1} │ {yCA,9:F1} │ {yCB,9:F1} │ {Z,6:F0} │ {D12,8:F2} │ {D22,8:F2} │");
        }

        private void PrintHeader()
        {
            Console.WriteLine("┌────────┬───────────┬───────────┬───────────┬───────────┬────────┬──────────┬──────────┐");
            Console.WriteLine("│   t    │    y0A    │    y0B    │    yCA    │    yCB    │   Z    │   D12    │   D22    │");
            Console.WriteLine("├────────┼───────────┼───────────┼───────────┼───────────┼────────┼──────────┼──────────┤");
        }

        private void PrintFooter()
        {
            Console.WriteLine("└────────┴───────────┴───────────┴───────────┴───────────┴────────┴──────────┴──────────┘");
        }

        private void ExportResultsToFile()
        {
            string filePath = "model_results.csv";
            using (StreamWriter writer = new StreamWriter(filePath))
            {
                writer.WriteLine("t;y0A;y0B;yCA;yCB;Z;D12;D22");
                for (int i = 0; i < timeHistory.Count; i++)
                {
                    writer.WriteLine($"{timeHistory[i]:F1};{y0AHistory[i]:F2};{y0BHistory[i]:F2};" +
                                     $"{yCAHistory[i]:F2};{yCBHistory[i]:F2};{ZHistory[i]:F2};" +
                                     $"{D12History[i]:F2};{D22History[i]:F2}");
                }
            }
        }

        public void Run()
        {
            Console.WriteLine("                        ПАРАМЕТРЫ МОДЕЛИ");
            Console.WriteLine("");
            Console.WriteLine($"  • Комплектность:           ПА = {PA}, ПВ = {PB}");
            Console.WriteLine($"  • Задержки 2-го звена:     Dmin = {Dmin}, Dср = {Davg}, Dmax = {Dmax}");
            Console.WriteLine($"  • Задержки 1 и 3 звена:    D1 = {D1}, D3 = {D3}");
            Console.WriteLine($"  • Допустимая разница:      dMдоп = {DeltaMuDop}");
            Console.WriteLine($"  • Коэффициент управления:  d = {DCoefBase}/|dM|");
            Console.WriteLine($"  • Шаг и время:             Δt = {DeltaT}, T = {Tmax}");
            Console.WriteLine($"  • Пополнение:              Q_A = {Q_A}, Q_B = {Q_B}");
            Console.WriteLine($"  • Порог пополнения:        {Gamma * Y0A0} (20%)");
            Console.WriteLine($"  • Порог остановки:         {Epsilon * Y0A0} (5%)");
            Console.WriteLine($"  • Количество шагов:        {Tmax / DeltaT}");

            PrintHeader();

            SetInitialConditions();
            SaveToHistory(0);
            PrintState(0);

            double prevD12 = D12;
            double prevD22 = D22;

            for (int step = 1; step <= Tmax / DeltaT; step++)
            {
                double currentTime = step * DeltaT;
                prevD12 = D12;
                prevD22 = D22;

                CalculateAssemblyRate();
                CalculateDelays(prevD12, prevD22);
                CalculateFlowRates();
                CheckReplenishmentAndStop(currentTime);
                UpdateLevels();
                SaveToHistory(currentTime);

                if (step % 5 == 0 || step == (int)(Tmax / DeltaT))
                {
                    PrintState(currentTime);
                }
            }

            PrintFooter();

            Console.WriteLine("\n══════════════════════════════════════════════════════════════════════════════════════");
            Console.WriteLine("                         ИТОГОВЫЕ РЕЗУЛЬТАТЫ");
            Console.WriteLine("══════════════════════════════════════════════════════════════════════════════════════");
            Console.WriteLine($"  • Выпущено готовых изделий С:     {Z:F0} шт.");
            Console.WriteLine($"  • Конечный уровень склада А:      {y0A:F2} ед.");
            Console.WriteLine($"  • Конечный уровень склада В:      {y0B:F2} ед.");
            Console.WriteLine($"  • Конечный уровень А в сборке:    {yCA:F2} ед.");
            Console.WriteLine($"  • Конечный уровень В в сборке:    {yCB:F2} ед.");
            Console.WriteLine($"  • Конечная задержка D12:          {D12:F2}");
            Console.WriteLine($"  • Конечная задержка D22:          {D22:F2}");

            if (lineAStopped)
                Console.WriteLine($"\n   ЛИНИЯ А БЫЛА ОСТАНОВЛЕНА (запас упал до {Epsilon * Y0A0} ед.)");
            if (lineBStopped)
                Console.WriteLine($"    ЛИНИЯ В БЫЛА ОСТАНОВЛЕНА (запас упал до {Epsilon * Y0A0} ед.)");

            Console.WriteLine($"\n  • Средний уровень А в сборке:    {yCAHistory.Average():F2} ед.");
            Console.WriteLine($"  • Средний уровень В в сборке:    {yCBHistory.Average():F2} ед.");
            Console.WriteLine("══════════════════════════════════════════════════════════════════════════════════════");

            ExportResultsToFile();
        }
    }
}