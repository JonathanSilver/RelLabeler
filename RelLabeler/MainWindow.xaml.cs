using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace RelLabeler
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        readonly List<RecordControl> records = new List<RecordControl>();

        readonly List<Tuple<string, string>> entityLabels = new List<Tuple<string, string>>();
        readonly List<Tuple<string, string>> predicateLabels = new List<Tuple<string, string>>();
        
        public SearchWindow searchWindow = null;

        public string filePath;
        public int idx = -1;

        string currentText;

        void Clear()
        {
            filePath = null;
            idx = -1;
            records.Clear();
            entityLabels.Clear();
            RecordsList.Items.Clear();
            SelectedSentence.Items.Clear();

            currentText = "";
            SentenceText.Document.Blocks.Clear();
        }

        void CreateLabelTable(SqliteConnection connection)
        {
            var command = connection.CreateCommand();
            command.CommandText = @"
                drop table if exists label;
            ";
            command.ExecuteNonQuery();
            command.CommandText = @"
                create table label (
                    code text not null,
                    name text not null,
                    type int not null
                );
            ";
            command.ExecuteNonQuery();
        }

        List<Tuple<string, string>> GetLabels(SqliteConnection connection, int type)
        {
            // if type == 0, return entity labels,
            // otherwise, return predicate labels
            List<Tuple<string, string>> labels = new List<Tuple<string, string>>();
            var command = connection.CreateCommand();
            command.CommandText = @"
                select code, name from label where type = $type order by code;
            ";
            command.Parameters.AddWithValue("$type", type);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    labels.Add(
                        new Tuple<string, string>(
                            reader.GetString(0), reader.GetString(1)));
                }
            }
            return labels;
        }

        void LoadLabels()
        {
            entityLabels.Clear();
            predicateLabels.Clear();
            using (var connection = new SqliteConnection($"Data Source={filePath}"))
            {
                connection.Open();
                try
                {
                    entityLabels.AddRange(
                        GetLabels(connection, 0)
                        );
                }
                catch (SqliteException)
                {
                    // assume this is triggered by missing the table
                    CreateLabelTable(connection);
                    entityLabels.AddRange(
                        GetLabels(connection, 0)
                        ); // redo the query
                }
                predicateLabels.AddRange(
                    GetLabels(connection, 1)
                    );
            }
            // perform reloading for each `RecordControl` combobox
            foreach (var record in records)
            {
                record.LoadLabels();
            }
        }

        void NewRecord()
        {
            int firstSelectedPos = -1;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].IsChecked)
                {
                    if (firstSelectedPos == -1)
                    {
                        firstSelectedPos = i;
                    }
                    records[i].IsChecked = false;
                }
            }
            RecordControl recordControl = new RecordControl(entityLabels, predicateLabels)
            {
                IsChecked = true
            };
            firstSelectedPos++;
            records.Insert(firstSelectedPos, recordControl);
            RecordsList.Items.Insert(firstSelectedPos, recordControl);
        }

        void DeleteSelectedRecords()
        {
            for (int i = records.Count - 1; i >= 0; i--)
            {
                if (records[i].IsChecked)
                {
                    RecordsList.Items.Remove(records[i]);
                    records.RemoveAt(i);
                }
            }

            ReloadText();
        }

        List<RecordControl> GetSelectedRecords()
        {
            List<RecordControl> recordControls = new List<RecordControl>();
            foreach (var record in records)
            {
                if (record.IsChecked)
                {
                    recordControls.Add(record);
                }
            }
            return recordControls;
        }

        void SaveCurrentRecords()
        {
            using (var connection = new SqliteConnection($"Data Source={filePath}"))
            {
                connection.Open();
                List<Tuple<string, string, string, string, string, string>> data
                    = new List<Tuple<string, string, string, string, string, string>>();
                foreach (var record in records)
                {
                    // the order matters!
                    // for forward compatibility
                    data.Add(
                        new Tuple<string, string, string, string, string, string>(
                            record.Subject,
                            record.Predicate,
                            record.Object,
                            record.SubjectLabel,
                            record.ObjectLabel,
                            record.PredicateLabel));
                }
                var command = connection.CreateCommand();
                command.CommandText = @"
                    update data set relations = $data where sentence = $text;
                ";
                command.Parameters.AddWithValue("$data",
                    JsonSerializer.Serialize(data,
                    new JsonSerializerOptions
                    {
                        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
                    }));
                command.Parameters.AddWithValue("$text", currentText);
                command.ExecuteNonQuery();
            }
        }

        FlowDocument GetDocument()
        {
            Paragraph paragraph = new Paragraph();

            List<string> entities = new List<string>();
            foreach (var record in records)
            {
                if (!entities.Contains(record.Subject))
                {
                    entities.Add(record.Subject);
                }
            }
            entities.Sort((x, y) => x.Length == y.Length ? x.CompareTo(y) : y.Length - x.Length);

            Color[] c = new Color[] { Colors.Red, Colors.Green, Colors.Blue };
            int j = 0;

            string word = null;
            if (searchWindow != null)
            {
                word = searchWindow.SearchBox.Text;
            }

            bool highlight = false;
            int highlightEnd = -1;
            for (int i = 0; i < currentText.Length; i++)
            {
                if (word != null)
                {
                    if (!highlight)
                    {
                        if (i + word.Length <= currentText.Length && currentText.Substring(i, word.Length) == word)
                        {
                            highlight = true;
                            highlightEnd = i + word.Length;
                        }
                    }
                    else if (i == highlightEnd)
                    {
                        highlight = false;
                    }
                }
                bool found = false;
                foreach (var e in entities)
                {
                    if (i + e.Length <= currentText.Length && currentText.Substring(i, e.Length) == e)
                    {
                        Color currC = c[(j++) % c.Length];
                        if (highlight)
                        {
                            if (highlightEnd >= i + e.Length)
                            {
                                // 0 [{1 2 3}] 4
                                Run run = new Run(e);
                                run.Foreground = new SolidColorBrush(currC);
                                run.Background = new SolidColorBrush(Colors.Yellow);
                                Bold bold = new Bold(run);
                                paragraph.Inlines.Add(bold);
                            }
                            else
                            {
                                // 0 [{1} 2 3] 4
                                Run run1 = new Run(currentText.Substring(i, highlightEnd - i));
                                run1.Foreground = new SolidColorBrush(currC);
                                run1.Background = new SolidColorBrush(Colors.Yellow);
                                Bold bold1 = new Bold(run1);
                                paragraph.Inlines.Add(bold1);
                                Run run2 = new Run(currentText.Substring(highlightEnd, i + e.Length - highlightEnd));
                                run2.Foreground = new SolidColorBrush(currC);
                                Bold bold2 = new Bold(run2);
                                paragraph.Inlines.Add(bold2);
                                highlight = false;
                            }
                        }
                        else
                        {
                            if (word != null)
                            {
                                int highlightBegin = -1;
                                for (int k = i + 1; k < i + e.Length; k++)
                                {
                                    if (k + word.Length <= currentText.Length && currentText.Substring(k, word.Length) == word)
                                    {
                                        highlight = true;
                                        highlightBegin = k;
                                        highlightEnd = k + word.Length;
                                        break;
                                    }
                                }
                                if (highlight)
                                {
                                    Run run1 = new Run(currentText.Substring(i, highlightBegin - i));
                                    run1.Foreground = new SolidColorBrush(currC);
                                    Bold bold1 = new Bold(run1);
                                    paragraph.Inlines.Add(bold1);
                                    if (highlightEnd >= i + e.Length)
                                    {
                                        // 0 [1 2 {3 4 5}] 6
                                        Run run2 = new Run(currentText.Substring(highlightBegin, i + e.Length - highlightBegin));
                                        run2.Foreground = new SolidColorBrush(currC);
                                        run2.Background = new SolidColorBrush(Colors.Yellow);
                                        Bold bold2 = new Bold(run2);
                                        paragraph.Inlines.Add(bold2);
                                    }
                                    else
                                    {
                                        // 0 [1 2 {3 4 5} 6 7 8 9] 10
                                        Run run2 = new Run(currentText.Substring(highlightBegin, highlightEnd - highlightBegin));
                                        run2.Foreground = new SolidColorBrush(currC);
                                        run2.Background = new SolidColorBrush(Colors.Yellow);
                                        Bold bold2 = new Bold(run2);
                                        paragraph.Inlines.Add(bold2);
                                        Run run3 = new Run(currentText.Substring(highlightEnd, i + e.Length - highlightEnd));
                                        run3.Foreground = new SolidColorBrush(currC);
                                        Bold bold3 = new Bold(run3);
                                        paragraph.Inlines.Add(bold3);
                                        highlight = false;
                                    }
                                }
                                else
                                {
                                    Run run = new Run(e);
                                    run.Foreground = new SolidColorBrush(currC);
                                    Bold bold = new Bold(run);
                                    paragraph.Inlines.Add(bold);
                                }
                            }
                            else
                            {
                                Run run = new Run(e);
                                run.Foreground = new SolidColorBrush(currC);
                                Bold bold = new Bold(run);
                                paragraph.Inlines.Add(bold);
                            }
                        }

                        found = true;
                        i += e.Length - 1;
                        break;
                    }
                }
                if (!found)
                {
                    Run run = new Run(currentText[i].ToString());
                    if (highlight)
                    {
                        run.Background = new SolidColorBrush(Colors.Yellow);
                    }
                    paragraph.Inlines.Add(run);
                }
            }

            FlowDocument document = new FlowDocument();
            document.Blocks.Add(paragraph);
            return document;
        }

        public void ReloadText()
        {
            double offset = SentenceText.VerticalOffset;
            SentenceText.Document = GetDocument();
            SentenceText.ScrollToVerticalOffset(offset);
        }

        void SelectSentence(int idx)
        {
            if (idx == -1) return;
            SaveCurrentRecords();
            this.idx = idx;
            SelectedSentence.SelectedIndex = idx;
            using (var connection = new SqliteConnection($"Data Source={filePath}"))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"
                    select * from data where line_id = $id;
                ";
                command.Parameters.AddWithValue("$id", (int)SelectedSentence.SelectedItem);
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        List<Tuple<string, string, string, string, string, string>> data;
                        if (reader.GetString(2) == "")
                        {
                            data = new List<Tuple<string, string, string, string, string, string>>();
                        }
                        else
                        {
                            data = JsonSerializer.Deserialize<
                                List<Tuple<string, string, string, string, string, string>>
                                >(reader.GetString(2));
                        }

                        records.Clear();
                        RecordsList.Items.Clear();
                        foreach (var tuple in data)
                        {
                            RecordControl record = new RecordControl(entityLabels, predicateLabels)
                            {
                                Subject = tuple.Item1,
                                Predicate = tuple.Item2,
                                Object = tuple.Item3,
                                SubjectLabel = tuple.Item4,
                                ObjectLabel = tuple.Item5,
                                PredicateLabel = tuple.Item6
                            };

                            if (searchWindow != null)
                            {
                                if (record.Subject.Contains(searchWindow.SearchBox.Text))
                                {
                                    record.IsChecked = true;
                                }
                            }

                            records.Add(record);
                            RecordsList.Items.Add(record);
                        }

                        currentText = reader.GetString(1);
                        SentenceText.Document = GetDocument();
                    }
                    if (reader.Read())
                    {
                        throw new InvalidDataException("Duplicated Sentences!");
                    }
                }
            }
        }

        void GoPrevious()
        {
            if (idx > 0)
            {
                SelectSentence(idx - 1);
            }
        }

        void GoNext()
        {
            if (idx + 1 < SelectedSentence.Items.Count)
            {
                SelectSentence(idx + 1);
            }
        }

        static void Swap<T>(ref T x, ref T y)
        {
            T z = x;
            x = y;
            y = z;
        }

        static void Swap<T>(T list, int x, int y) where T : System.Collections.IList
        {
            if (x == y) return;
            if (x > y) Swap(ref x, ref y);
            // x < y
            object obj_x = list[x];
            object obj_y = list[y];
            list.RemoveAt(y);
            list.RemoveAt(x);
            list.Insert(x, obj_y);
            list.Insert(y, obj_x);
        }

        public void OpenFile(string fileName, int selectedSentence, bool isLineId = false)
        {
            if (idx != -1)
            {
                SaveCurrentRecords();
            }
            Clear();
            filePath = fileName;
            if (filePath.EndsWith(".db"))
            {
                using (var connection = new SqliteConnection($"Data Source={filePath}"))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                            select line_id from data where line_id is not null order by line_id;
                        ";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            SelectedSentence.Items.Add(reader.GetInt32(0));
                        }
                    }
                }
            }
            else
            {
                filePath += ".db";
                if (!File.Exists(filePath))
                {
                    using (var connection = new SqliteConnection($"Data Source={filePath}"))
                    {
                        connection.Open();
                        var command = connection.CreateCommand();
                        command.CommandText =
                        @"
                                create table data (
                                    line_id int unique,
                                    sentence text not null unique,
                                    relations text not null
                                );
                            ";
                        command.ExecuteNonQuery();
                    }
                }
                using (StreamReader reader = new StreamReader(fileName))
                {
                    using (var connection = new SqliteConnection($"Data Source={filePath}"))
                    {
                        connection.Open();
                        var command = connection.CreateCommand();
                        command.CommandText = @"
                                update data set line_id = null;
                            ";
                        command.ExecuteNonQuery();
                        int lineId = 0;
                        while (reader.Peek() != -1)
                        {
                            string text = reader.ReadLine();
                            if (text != "")
                            {
                                command.CommandText = @"
                                        select count(*) from data where sentence = $text;
                                    ";
                                command.Parameters.Clear();
                                command.Parameters.AddWithValue("$text", text);
                                object r = command.ExecuteScalar();
                                lineId++;
                                if (Convert.ToInt32(r) == 0)
                                {
                                    command.CommandText = @"
                                            insert into data (line_id, sentence, relations) values ($id, $text, '');
                                        ";
                                }
                                else
                                {
                                    command.CommandText = @"
                                            update data set line_id = $id where sentence = $text;
                                        ";
                                }
                                command.Parameters.Clear();
                                command.Parameters.AddWithValue("$id", lineId);
                                command.Parameters.AddWithValue("$text", text);
                                command.ExecuteNonQuery();
                                SelectedSentence.Items.Add(lineId);
                            }
                        }
                    }
                }
            }
            Title = "RelLabeler - " + filePath;
            if (isLineId)
            {
                SelectedSentence.SelectedItem = selectedSentence;
            }
            else
            {
                if (selectedSentence < SelectedSentence.Items.Count)
                {
                    SelectSentence(selectedSentence);
                }
            }

            SaveButton.IsEnabled = true;
            ExportButton.IsEnabled = true;
            EntityLabelManagerButton.IsEnabled = true;
            PredicateLabelManagerButton.IsEnabled = true;

            PreviousSentenceButton.IsEnabled = true;
            SelectedSentence.IsEnabled = true;
            NextSentenceButton.IsEnabled = true;
            NewRecordButton.IsEnabled = true;
            DeleteRecordButton.IsEnabled = true;

            SearchButton.IsEnabled = true;

            LoadLabels();
        }

        private void NewRecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (idx != -1)
            {
                NewRecord();
            }
        }

        private void DeleteRecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (idx != -1)
            {
                DeleteSelectedRecords();
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (idx != -1)
            {
                SaveCurrentRecords();
            }
        }

        private void SelectedSentence_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectSentence(SelectedSentence.SelectedIndex);
        }

        private void PreviousSentenceButton_Click(object sender, RoutedEventArgs e)
        {
            if (idx != -1)
            {
                GoPrevious();
            }
        }

        private void NextSentenceButton_Click(object sender, RoutedEventArgs e)
        {
            if (idx != -1)
            {
                GoNext();
            }
        }

        private void SentenceText_KeyDown(object sender, KeyEventArgs e)
        {
            if (idx == -1) return;
            if (e.Key == Key.A)
            {
                var controls = GetSelectedRecords();
                foreach (var record in controls)
                {
                    record.Subject = SentenceText.Selection.Text;
                }

                ReloadText();
            }
            else if (e.Key == Key.S)
            {
                var controls = GetSelectedRecords();
                foreach (var record in controls)
                {
                    record.Predicate = SentenceText.Selection.Text;
                }
            }
            else if (e.Key == Key.D)
            {
                var controls = GetSelectedRecords();
                foreach (var record in controls)
                {
                    record.Object = SentenceText.Selection.Text;
                }
            }
            else if (e.Key == Key.F)
            {
                NewRecord();
            }
            else if (e.Key == Key.R)
            {
                GoPrevious();
            }
            else if (e.Key == Key.T)
            {
                GoNext();
            }
            else if (e.Key == Key.Q)
            {
                int selected = -1;
                for (int i = 0; i < records.Count; i++)
                {
                    if (records[i].IsChecked)
                    {
                        if (selected == -1)
                        {
                            selected = i;
                        }
                        else
                        {
                            return;
                        }
                    }
                }
                if (selected != -1 && selected > 0)
                {
                    if (Keyboard.IsKeyDown(Key.LeftCtrl))
                    {
                        Swap(records, selected, selected - 1);
                        Swap(RecordsList.Items, selected, selected - 1);
                    }
                    else
                    {
                        records[selected].IsChecked = false;
                        records[selected - 1].IsChecked = true;
                    }
                }
            }
            else if (e.Key == Key.W)
            {
                int selected = -1;
                for (int i = 0; i < records.Count; i++)
                {
                    if (records[i].IsChecked)
                    {
                        if (selected == -1)
                        {
                            selected = i;
                        }
                        else
                        {
                            return;
                        }
                    }
                }
                if (selected != -1 && selected + 1 < records.Count)
                {
                    if (Keyboard.IsKeyDown(Key.LeftCtrl))
                    {
                        Swap(records, selected, selected + 1);
                        Swap(RecordsList.Items, selected, selected + 1);
                    }
                    else
                    {
                        records[selected].IsChecked = false;
                        records[selected + 1].IsChecked = true;
                    }
                }
            }
            else if (e.Key == Key.E)
            {
                bool allSelected = true;
                foreach (var record in records)
                {
                    if (!record.IsChecked)
                    {
                        allSelected = false;
                        break;
                    }
                }
                foreach (var record in records)
                {
                    record.IsChecked = !allSelected;
                }
            }
            else if (e.Key == Key.G)
            {
                SaveCurrentRecords();
            }
            else if (e.Key == Key.B)
            {
                DeleteSelectedRecords();
            }
        }

        private void SentenceText_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (idx == -1) return;
            if (e.Key == Key.Space)
            {
                if (records.Count > 0)
                {
                    records[0].IsChecked = !records[0].IsChecked;
                }
            }
        }

        private void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Text/DB files (*.txt, *.db)|*.txt;*.db|All files (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };
            if (openFileDialog.ShowDialog() == true)
            {
                OpenFile(openFileDialog.FileName, 0);
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (idx == -1) return;
            SaveCurrentRecords();
            Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                DefaultExt = ".json",
                AddExtension = true,
                Filter = "Json files (*.json)|*.json|All files (*.*)|*.*",
            };
            if (saveFileDialog.ShowDialog() == true)
            {
                var file = saveFileDialog.OpenFile();
                using (StreamWriter writer = new StreamWriter(file))
                {
                    using (var connection = new SqliteConnection($"Data Source={filePath}"))
                    {
                        connection.Open();
                        var command = connection.CreateCommand();
                        command.CommandText = @"
                            select * from data where line_id is not null order by line_id;
                        ";
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                List<Tuple<string, string, string, string, string, string>> list;
                                if (reader.GetString(2) == "")
                                {
                                    list = new List<Tuple<string, string, string, string, string, string>>();
                                }
                                else
                                {
                                    list = JsonSerializer.Deserialize<
                                        List<Tuple<string, string, string, string, string, string>>
                                        >(reader.GetString(2));
                                }
                                List<Dictionary<string, string>> res = new List<Dictionary<string, string>>();
                                foreach (var tuple in list)
                                {
                                    res.Add(new Dictionary<string, string>
                                    {
                                        { "subject", tuple.Item1 },
                                        { "predicate", tuple.Item2 },
                                        { "object", tuple.Item3 },
                                        { "subject_type", tuple.Item4 },
                                        { "object_type", tuple.Item5 },
                                        { "predicate_type", tuple.Item6 }
                                    });
                                }
                                Dictionary<string, object> data = new Dictionary<string, object>
                                {
                                    { "text", reader.GetString(1) },
                                    { "spos", res }
                                };
                                writer.WriteLine(JsonSerializer.Serialize(data,
                                    new JsonSerializerOptions
                                    {
                                        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
                                    }));
                            }
                        }
                    }
                    writer.Flush();
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (searchWindow != null)
            {
                if (MessageBox.Show(
                    "This is the main window. Closing it will close other opening windows as well. Are you sure you want to close it anyway?",
                    "Closing Confirmation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Exclamation,
                    MessageBoxResult.No) == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }
                else
                {
                    searchWindow.enableClosingCheck = false;
                    searchWindow.Close();
                }
            }
            if (idx != -1)
            {
                SaveCurrentRecords();
            }
        }

        private void EntityLabelManagerButton_Click(object sender, RoutedEventArgs e)
        {
            new LabelManager(filePath, entityLabels, 0).ShowDialog();
            LoadLabels();
        }

        private void PredicateLabelManagerButton_Click(object sender, RoutedEventArgs e)
        {
            new LabelManager(filePath, predicateLabels, 1).ShowDialog();
            LoadLabels();
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (searchWindow == null)
                searchWindow = new SearchWindow(this);
            searchWindow.Show();
            searchWindow.Activate();
        }
    }
}
