using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RelLabeler
{
    /// <summary>
    /// SearchWindow.xaml 的交互逻辑
    /// </summary>
    public partial class SearchWindow : Window
    {
        readonly MainWindow mainWindow;

        readonly List<Tuple<string, int, string>> result = new List<Tuple<string, int, string>>();

        // <search word, main window status, search result, selected result>
        readonly List<
            Tuple<string, Tuple<string, int>, List<Tuple<string, int, string>>, int>
            > history = new List<Tuple<string, Tuple<string, int>, List<Tuple<string, int, string>>, int>>();
        int _historyPointer = -1;
        bool enableSelectionChangedDetection = true;
        public bool enableClosingCheck = true;

        public string SearchText { get; set; }

        public SearchWindow(MainWindow mainWindow)
        {
            InitializeComponent();

            this.mainWindow = mainWindow;

            history.Add(GetCurrentStatus());
            HistoryPointer = 0;

            SearchText = "";
        }

        int HistoryPointer
        {
            get { return _historyPointer; }
            set
            {
                _historyPointer = value;
                GoBackButton.IsEnabled = value > 0;
                GoForwardButton.IsEnabled = value + 1 < history.Count;
            }
        }

        Tuple<string, Tuple<string, int>, List<Tuple<string, int, string>>, int> GetCurrentStatus()
        {
            return new Tuple<string, Tuple<string, int>, List<Tuple<string, int, string>>, int>(
                    SearchText,
                    new Tuple<string, int>(mainWindow.filePath, mainWindow.idx),
                    new List<Tuple<string, int, string>>(result),
                    ResultList.SelectedIndex);
        }

        void RestoreFromHistory()
        {
            var record = history[HistoryPointer];
            SearchText = SearchBox.Text = record.Item1;
            mainWindow.OpenFile(record.Item2.Item1, record.Item2.Item2);
            mainWindow.SelectFirstMatchedText(SearchText);
            result.Clear();
            result.AddRange(record.Item3);
            ResultList.Items.Clear();
            foreach (var item in record.Item3)
            {
                ResultList.Items.Add(item.Item3);
            }
            enableSelectionChangedDetection = false;
            ResultList.SelectedIndex = record.Item4;
            enableSelectionChangedDetection = true;
        }

        public void Search()
        {
            if (SearchBox.Text == "")
                return;

            history.RemoveRange(HistoryPointer + 1, history.Count - HistoryPointer - 1);
            history[HistoryPointer] = GetCurrentStatus();

            ResultList.Items.Clear();
            result.Clear();
            FileInfo fileInfo = new FileInfo(mainWindow.filePath);
            foreach (var f in fileInfo.Directory.GetFiles("*.db"))
            {
                using (var connection = new SqliteConnection($"Data Source={f.FullName}"))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        select line_id, relations from data
                            where line_id is not null and sentence like $pattern order by line_id;
                    ";
                    command.Parameters.AddWithValue("$pattern", "%" + SearchBox.Text + "%");
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            List<DataRecord> data;
                            if (reader.GetString(1) == "")
                            {
                                data = new List<DataRecord>();
                            }
                            else
                            {
                                data = JsonSerializer.Deserialize<
                                    List<DataRecord>
                                    >(reader.GetString(1));
                            }
                            string matched = "";
                            foreach (var record in data)
                            {
                                if (!mainWindow.showAnnotations && record.Item8.GetValueOrDefault(false))
                                {
                                    continue;
                                }
                                if (record.Item1.ToLower().Contains(SearchBox.Text.ToLower()))
                                {
                                    if (matched != "")
                                    {
                                        matched += ", ";
                                    }
                                    matched += record.Item1;
                                }
                            }
                            matched = f.Name + " (" + reader.GetInt32(0) + ")" + (matched == "" ? "" : " : " + matched);
                            ResultList.Items.Add(matched);
                            result.Add(new Tuple<string, int, string>(f.FullName, reader.GetInt32(0), matched));
                        }
                    }
                }
            }

            SearchText = SearchBox.Text;
            history.Add(GetCurrentStatus());
            HistoryPointer = history.Count - 1;

            mainWindow.ReloadText();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            mainWindow.searchWindow = null;

            mainWindow.ReloadText();
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            Search();
        }

        private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (enableSelectionChangedDetection && ResultList.SelectedIndex != -1)
            {
                mainWindow.OpenFile(result[ResultList.SelectedIndex].Item1, result[ResultList.SelectedIndex].Item2, true);
                mainWindow.Activate();
                mainWindow.SelectFirstMatchedText(SearchText);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (enableClosingCheck && MessageBox.Show(
                "The search window contains your search history, which will be lost after you close it. Are you sure you want to proceed anyway?",
                "Closing Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Exclamation,
                MessageBoxResult.No) == MessageBoxResult.No)
            {
                e.Cancel = true;
                return;
            }
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Search();
            }
        }

        private void GoBackButton_Click(object sender, RoutedEventArgs e)
        {
            history[HistoryPointer] = GetCurrentStatus();
            HistoryPointer--;
            RestoreFromHistory();
        }

        private void GoForwardButton_Click(object sender, RoutedEventArgs e)
        {
            history[HistoryPointer] = GetCurrentStatus();
            HistoryPointer++;
            RestoreFromHistory();
        }
    }
}
