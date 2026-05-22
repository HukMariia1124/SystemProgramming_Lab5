using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace WpfApp5;

public partial class MainWindow : Window
{
    private const int MinProcesses = 2;
    private const int MaxProcesses = 4;
    private const int MinResources = 1;
    private const int MaxResources = 3;

    private readonly Random _random = new();
    private int[] _total = [8, 10, 9]; // E
    private int[] _available = [0, 0, 0]; // A
    private int[,] _allocation = new int[4, 3]; // C
    private int[,] _maximum = new int[4, 3];
    private int[,] _need = new int[4, 3];

    public MainWindow()
    {
        InitializeComponent();
        GenerateScenario();
    }

    // Запускає аналіз поточних таблиць після ручного редагування.
    private void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ReadStateFromUi())
        {
            return;
        }

        AnalyzeCurrentState(writeHeader: true);
    }

    // Очищає журнал моделювання.
    private void ClearLogButton_Click(object sender, RoutedEventArgs e) => LogBox.Document.Blocks.Clear();

    // Змінюють кількість процесів і ресурсів у дозволених межах.
    private void ProcessUp_Click(object sender, RoutedEventArgs e) => ChangeTextValue(ProcessCountText, 1, MinProcesses, MaxProcesses);
    private void ProcessDown_Click(object sender, RoutedEventArgs e) => ChangeTextValue(ProcessCountText, -1, MinProcesses, MaxProcesses);
    private void ResourceUp_Click(object sender, RoutedEventArgs e) => ChangeTextValue(ResourceCountText, 1, MinResources, MaxResources);
    private void ResourceDown_Click(object sender, RoutedEventArgs e) => ChangeTextValue(ResourceCountText, -1, MinResources, MaxResources);

    // Генерує випадкові параметри
    private void PresetAdvanced_Click(object sender, RoutedEventArgs e)
    {
        var processCount = _random.Next(MinProcesses, MaxProcesses + 1);
        var resourceCount = _random.Next(MinResources, MaxResources + 1);
        ProcessCountText.Text = processCount.ToString();
        ResourceCountText.Text = resourceCount.ToString();
        TotalsText.Text = string.Join(" ", Enumerable.Range(0, resourceCount).Select(_ => _random.Next(6, 13)));
        GenerateScenario();
    }

    // Після редагування клітинки оновлює Need і Available.
    private void MatrixGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (ReadStateFromUi(showErrors: false))
            {
                RecalculateDerivedState();
                BindReadOnlyViews();
            }
        });
    }

    // Підписує рядки таблиць як P0, P1, P2 тощо.
    private void MatrixGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.Header = $"P{e.Row.GetIndex()}";
    }

    // Дозволяє вводити тільки цифри в числові поля.
    private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
    }

    // Створює випадковий стан системи: Allocation, Max і Need.
    private void GenerateScenario()
    {
        if (!ReadDimensionsAndVectors(out var processCount, out var resourceCount, showErrors: true))
        {
            return;
        }

        _allocation = new int[processCount, resourceCount];
        _maximum = new int[processCount, resourceCount];
        _need = new int[processCount, resourceCount];

        for (var j = 0; j < resourceCount; j++)
        {
            var remaining = _total[j];
            for (var i = 0; i < processCount; i++)
            {
                // Частину ресурсу залишаємо іншим процесам, щоб розподіл був реалістичнішим.
                var processesLeft = processCount - i - 1;
                var reserve = processesLeft == 0 ? 0 : _random.Next(0, Math.Min(remaining, processesLeft * 2) + 1);
                var maxAlloc = Math.Max(0, remaining - reserve);
                _allocation[i, j] = _random.Next(0, Math.Min(maxAlloc, 4) + 1);
                remaining -= _allocation[i, j];
            }
        }

        for (var i = 0; i < processCount; i++)
        {
            for (var j = 0; j < resourceCount; j++)
            {
                var extraNeed = _random.Next(0, Math.Max(2, _total[j] / 2) + 1);
                _maximum[i, j] = Math.Min(_total[j], _allocation[i, j] + extraNeed);
                _need[i, j] = _maximum[i, j] - _allocation[i, j];
            }
        }

        RecalculateDerivedState();
        BindAllGrids();
        AddLog("Згенеровано новий стан системи.", Brushes.LightBlue);
        AnalyzeCurrentState(writeHeader: false);
    }

    // Зчитує дані з інтерфейсу і перевіряє коректність матриць.
    private bool ReadStateFromUi(bool showErrors = true)
    {
        if (!ReadDimensionsAndVectors(out var processCount, out var resourceCount, showErrors))
        {
            return false;
        }

        _allocation = ReadMatrixFromGrid(AllocationGrid, processCount, resourceCount);
        _maximum = ReadMatrixFromGrid(MaxGrid, processCount, resourceCount);

        for (var i = 0; i < processCount; i++)
        {
            for (var j = 0; j < resourceCount; j++)
            {
                if (_allocation[i, j] > _maximum[i, j])
                {
                    if (showErrors)
                    {
                        MessageBox.Show($"Для P{i}, R{j + 1}: виділено більше, ніж максимум.", "Некоректна матриця",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    return false;
                }
            }
        }

        RecalculateDerivedState();
        BindReadOnlyViews();
        return true;
    }

    // Зчитує кількість процесів, ресурсів і вектор E.
    private bool ReadDimensionsAndVectors(out int processCount, out int resourceCount, bool showErrors)
    {
        processCount = Clamp(ParseSingleNumber(ProcessCountText.Text, 4), MinProcesses, MaxProcesses);
        resourceCount = Clamp(ParseSingleNumber(ResourceCountText.Text, 3), MinResources, MaxResources);
        ProcessCountText.Text = processCount.ToString();
        ResourceCountText.Text = resourceCount.ToString();

        _total = ParseVector(TotalsText.Text, resourceCount, defaultValue: 8);
        TotalsText.Text = string.Join(" ", _total);

        if (_total.Any(v => v <= 0))
        {
            if (showErrors)
            {
                MessageBox.Show("Загальна кількість кожного ресурсу має бути більшою за нуль.", "Некоректні дані",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return false;
        }

        return true;
    }

    // Оновлює дані, запускає алгоритм банкіра і показує результат.
    private void AnalyzeCurrentState(bool writeHeader)
    {
        RecalculateDerivedState();
        BindReadOnlyViews();

        if (writeHeader)
        {
            AddLog("=== Аналіз поточного стану ===", Brushes.White);
        }

        LogInitialCalculations();

        var safety = CheckSafety();
        LogSafetyTrace(safety);

        if (safety.IsSafe)
        {
            AddLog($"Алгоритм банкіра: безпечний стан. Можлива послідовність завершення: {FormatProcessSequence(safety.Sequence)}.", Brushes.LightGreen);
        }
        else
        {
            AddLog($"Алгоритм банкіра: небезпечний стан. Процеси {FormatProcessSequence(safety.Unfinished)} можуть залишитися без ресурсів.", Brushes.Orange);
        }

        if (safety.IsSafe)
        {
            SetStatus("⬤ Безпечний стан", "Алгоритм банкіра знайшов послідовність завершення всіх процесів.", "#1E3A1E", "#A6E3A1");
        }
        else
        {
            SetStatus("⬤ Небезпечний стан", "Алгоритм банкіра не знайшов послідовність, у якій усі процеси можуть завершитися.", "#3A321E", "#F9E2AF");
        }
    }

    // Пояснює, як з матриць отримуються Available і Need.
    private void LogInitialCalculations()
    {
        AddLog($"Загальний вектор ресурсів: E = {FormatVector(_total)}.", Brushes.LightBlue);

        for (var j = 0; j < _total.Length; j++)
        {
            var allocated = 0;
            for (var i = 0; i < _allocation.GetLength(0); i++)
            {
                allocated += _allocation[i, j];
            }

            AddLog($"R{j + 1}: виділено {allocated}, тому A{j + 1} = E{j + 1} - C = {_total[j]} - {allocated} = {_available[j]}.", Brushes.LightBlue);
        }

        AddLog($"Отже Available A = {FormatVector(_available)}.", Brushes.LightBlue);
        AddLog("Матриця Need рахується для кожного процесу за формулою Need = Max - C.", Brushes.LightBlue);

        for (var i = 0; i < _allocation.GetLength(0); i++)
        {
            AddLog($"P{i}: Need = {FormatVector(GetRow(_maximum, i))} - {FormatVector(GetRow(_allocation, i))} = {FormatVector(GetRow(_need, i))}.", Brushes.LightBlue);
        }
    }

    // Виводить у журнал покрокову роботу алгоритму банкіра.
    private void LogSafetyTrace(SafetyResult safety)
    {
        AddLog($"Початок перевірки: Work = Available = {FormatVector(safety.InitialWork)}.", Brushes.LightBlue);

        foreach (var step in safety.Steps)
        {
            AddLog(
                $"Крок {step.Step}: для P{step.Process} виконується Need = {FormatVector(step.Need)} <= Work = {FormatVector(step.WorkBefore)}.",
                Brushes.LightGreen);
            AddLog(
                $"P{step.Process} може завершитися і повертає Allocation = {FormatVector(step.Allocation)}; новий Work = {FormatVector(step.WorkAfter)}.",
                Brushes.LightGreen);
        }

        if (!safety.IsSafe)
        {
            AddLog("Зупинка: немає незавершеного процесу, для якого Need <= Work.", Brushes.Orange);
            foreach (var process in safety.Unfinished)
            {
                AddLog($"P{process}: Need = {FormatVector(GetRow(_need, process))}, поточний Work = {FormatVector(safety.FinalWork)}.", Brushes.Orange);
            }
        }
    }

    // Перевіряє, чи існує безпечна послідовність завершення процесів.
    private SafetyResult CheckSafety()
    {
        var processCount = _allocation.GetLength(0);
        var resourceCount = _allocation.GetLength(1);
        var work = (int[])_available.Clone();
        var initialWork = (int[])work.Clone();
        var finish = new bool[processCount];
        var sequence = new List<int>();
        var steps = new List<SafetyStep>();

        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < processCount; i++)
            {
                // Процес можна виконати, якщо його Need не перевищує поточний Work.
                if (finish[i] || !RowLessOrEqual(_need, i, work))
                {
                    continue;
                }

                var workBefore = (int[])work.Clone();
                var need = GetRow(_need, i);
                var allocation = GetRow(_allocation, i);
                for (var j = 0; j < resourceCount; j++)
                {
                    // Після уявного завершення процес повертає всі виділені ресурси.
                    work[j] += _allocation[i, j];
                }
                finish[i] = true;
                sequence.Add(i);
                steps.Add(new SafetyStep(steps.Count + 1, i, workBefore, need, allocation, (int[])work.Clone()));
                changed = true;
            }
        }

        var unfinished = Enumerable.Range(0, processCount).Where(i => !finish[i]).ToList();
        return new SafetyResult(unfinished.Count == 0, sequence, unfinished, initialWork, (int[])work.Clone(), steps);
    }

    // Перераховує Need і Available на основі поточних матриць.
    private void RecalculateDerivedState()
    {
        var processCount = _allocation.GetLength(0);
        var resourceCount = _allocation.GetLength(1);
        _need = new int[processCount, resourceCount];
        _available = new int[resourceCount];

        for (var j = 0; j < resourceCount; j++)
        {
            var allocated = 0;
            for (var i = 0; i < processCount; i++)
            {
                _allocation[i, j] = Math.Max(0, _allocation[i, j]);
                _maximum[i, j] = Math.Max(_allocation[i, j], _maximum[i, j]);
                _need[i, j] = _maximum[i, j] - _allocation[i, j];
                allocated += _allocation[i, j];
            }
            _available[j] = Math.Max(0, _total[j] - allocated);
        }
    }

    // Прив'язує всі матриці до таблиць інтерфейсу.
    private void BindAllGrids()
    {
        BindEditableMatrix(AllocationGrid, _allocation);
        BindEditableMatrix(MaxGrid, _maximum);
        BindReadOnlyViews();
    }

    // Оновлює таблицю Need і текстові вектори.
    private void BindReadOnlyViews()
    {
        BindReadOnlyMatrix(NeedGrid, _need);
        TotalsView.Text = $"E = {FormatVector(_total)}";
        AvailableView.Text = $"A = {FormatVector(_available)}";
    }

    // Створює редаговану таблицю для матриці.
    private static void BindEditableMatrix(DataGrid grid, int[,] matrix)
    {
        grid.Columns.Clear();
        for (var j = 0; j < matrix.GetLength(1); j++)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = $"R{j + 1}",
                Binding = new System.Windows.Data.Binding($"[{j}]")
                {
                    Mode = System.Windows.Data.BindingMode.TwoWay,
                    UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
                },
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        }

        grid.ItemsSource = ToRows(matrix);
    }

    // Створює таблицю тільки для перегляду.
    private static void BindReadOnlyMatrix(DataGrid grid, int[,] matrix)
    {
        grid.Columns.Clear();
        for (var j = 0; j < matrix.GetLength(1); j++)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = $"R{j + 1}",
                Binding = new System.Windows.Data.Binding($"[{j}]"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        }

        grid.ItemsSource = ToRows(matrix);
    }

    // Перетворює двовимірний масив у рядки для DataGrid.
    private static ObservableCollection<ObservableCollection<int>> ToRows(int[,] matrix)
    {
        var rows = new ObservableCollection<ObservableCollection<int>>();
        for (var i = 0; i < matrix.GetLength(0); i++)
        {
            var row = new ObservableCollection<int>();
            for (var j = 0; j < matrix.GetLength(1); j++)
            {
                row.Add(matrix[i, j]);
            }
            rows.Add(row);
        }
        return rows;
    }

    // Зчитує значення з DataGrid назад у матрицю.
    private static int[,] ReadMatrixFromGrid(DataGrid grid, int processCount, int resourceCount)
    {
        var result = new int[processCount, resourceCount];
        for (var i = 0; i < processCount; i++)
        {
            if (grid.Items[i] is not ObservableCollection<int> row)
            {
                continue;
            }

            for (var j = 0; j < resourceCount; j++)
            {
                result[i, j] = j < row.Count ? Math.Max(0, row[j]) : 0;
            }
        }
        return result;
    }

    // Парсить вектор ресурсів із рядка на зразок "8 10 9".
    private static int[] ParseVector(string text, int length, int defaultValue)
    {
        var values = Regex.Matches(text, @"\d+")
            .Select(match => int.Parse(match.Value))
            .ToList();

        while (values.Count < length)
        {
            values.Add(defaultValue);
        }

        return values.Take(length).ToArray();
    }

    // Парсить одне число з текстового поля.
    private static int ParseSingleNumber(string text, int defaultValue)
    {
        var match = Regex.Match(text, @"\d+");
        return match.Success ? int.Parse(match.Value) : defaultValue;
    }

    // Обмежує число мінімальним і максимальним значенням.
    private static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));

    // Перевіряє поелементну умову рядок матриці <= вектор.
    private static bool RowLessOrEqual(int[,] matrix, int row, int[] vector)
    {
        for (var j = 0; j < matrix.GetLength(1); j++)
        {
            if (matrix[row, j] > vector[j])
            {
                return false;
            }
        }
        return true;
    }

    // Повертає один рядок матриці як вектор.
    private static int[] GetRow(int[,] matrix, int row)
    {
        var values = new int[matrix.GetLength(1)];
        for (var j = 0; j < matrix.GetLength(1); j++)
        {
            values[j] = matrix[row, j];
        }
        return values;
    }

    // Форматує вектор для виводу в інтерфейсі та лозі.
    private static string FormatVector(IEnumerable<int> vector) => $"[{string.Join(", ", vector)}]";

    // Форматує список процесів як <P0, P1, P2>.
    private static string FormatProcessSequence(IEnumerable<int> processes)
    {
        var list = processes.Select(i => $"P{i}").ToList();
        return list.Count == 0 ? "немає" : $"<{string.Join(", ", list)}>";
    }

    // Змінює числове поле на один крок.
    private void ChangeTextValue(TextBox textBox, int delta, int min, int max)
    {
        var value = Clamp(ParseSingleNumber(textBox.Text, min) + delta, min, max);
        textBox.Text = value.ToString();
    }

    // Додає повідомлення в журнал моделювання.
    private void AddLog(string text, Brush brush)
    {
        var paragraph = new Paragraph(new Run($"[{DateTime.Now:HH:mm:ss}] {text}") { Foreground = brush })
        {
            Margin = new Thickness(0, 0, 0, 4)
        };
        LogBox.Document.Blocks.Add(paragraph);
        LogBox.ScrollToEnd();
    }

    // Оновлює кольоровий блок стану системи.
    private void SetStatus(string title, string detail, string backgroundHex, string foregroundHex)
    {
        StatusPanel.Background = (Brush)new BrushConverter().ConvertFromString(backgroundHex)!;
        StatusText.Foreground = (Brush)new BrushConverter().ConvertFromString(foregroundHex)!;
        StatusText.Text = title;
        StatusDetailText.Text = detail;
    }

    // Один крок уявного завершення процесу в алгоритмі банкіра.
    private sealed record SafetyStep(int Step, int Process, int[] WorkBefore, int[] Need, int[] Allocation, int[] WorkAfter);

    // Результат перевірки безпечності системи.
    private sealed record SafetyResult(
        bool IsSafe,
        List<int> Sequence,
        List<int> Unfinished,
        int[] InitialWork,
        int[] FinalWork,
        List<SafetyStep> Steps);
}
