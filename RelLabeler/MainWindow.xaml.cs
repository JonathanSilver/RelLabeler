using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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

        readonly List<string> entityLabels = new List<string>();
        readonly List<string> predicateLabels = new List<string>();

        string filePath;
        int idx = -1;

        void Clear()
        {
            filePath = null;
            idx = -1;
            records.Clear();
            entityLabels.Clear();
            RecordsList.Items.Clear();
            SelectedSentence.Items.Clear();
            SentenceText.Text = "";
        }

        void CreateLabelTable(SqliteConnection connection)
        {
            var command = connection.CreateCommand();
            command.CommandText = @"
                create table label (
                    name text not null,
                    type int not null
                );
            ";
            command.ExecuteNonQuery();
        }

        List<string> GetLabels(SqliteConnection connection, int type)
        {
            // if type == 0, return entity labels,
            // otherwise, return predicate labels
            List<string> labels = new List<string>();
            var command = connection.CreateCommand();
            command.CommandText = @"
                select * from label where type = $type;
            ";
            command.Parameters.AddWithValue("$type", type);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    labels.Add(reader.GetString(0));
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
                List<Tuple<string, string, string, string, string>> data = new List<Tuple<string, string, string, string, string>>();
                foreach (var record in records)
                {
                    data.Add(
                        new Tuple<string, string, string, string, string>(
                            record.Subject,
                            record.Predicate,
                            record.Object,
                            record.SubjectLabel,
                            record.ObjectLabel));
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
                command.Parameters.AddWithValue("$text", SentenceText.Text);
                command.ExecuteNonQuery();
            }
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
                        SentenceText.Text = reader.GetString(1);
                        List<Tuple<string, string, string, string, string>> data;
                        if (reader.GetString(2) == "")
                        {
                            data = new List<Tuple<string, string, string, string, string>>();
                        }
                        else
                        {
                            data = JsonSerializer.Deserialize<List<Tuple<string, string, string, string, string>>>(reader.GetString(2));
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
                                ObjectLabel = tuple.Item5
                            };
                            records.Add(record);
                            RecordsList.Items.Add(record);
                        }
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
                    record.Subject = SentenceText.SelectedText;
                }
            }
            else if (e.Key == Key.S)
            {
                var controls = GetSelectedRecords();
                foreach (var record in controls)
                {
                    record.Predicate = SentenceText.SelectedText;
                }
            }
            else if (e.Key == Key.D)
            {
                var controls = GetSelectedRecords();
                foreach (var record in controls)
                {
                    record.Object = SentenceText.SelectedText;
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
            if (idx != -1)
            {
                SaveCurrentRecords();
            }
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Text/DB files (*.txt, *.db)|*.txt;*.db|All files (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };
            if (openFileDialog.ShowDialog() == true)
            {
                Clear();
                filePath = openFileDialog.FileName;
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
                    var fileStream = openFileDialog.OpenFile();
                    using (StreamReader reader = new StreamReader(fileStream))
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
                if (SelectedSentence.Items.Count > 0)
                {
                    SelectSentence(0);
                }

                SaveButton.IsEnabled = true;
                ExportButton.IsEnabled = true;
                EntityLabelManagerButton.IsEnabled = true;
                PredicateLabelManagerButton.IsEnabled = true;

                LoadLabels();
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
                                List<Tuple<string, string, string>> list;
                                if (reader.GetString(2) == "")
                                {
                                    list = new List<Tuple<string, string, string>>();
                                }
                                else
                                {
                                    list = JsonSerializer.Deserialize<List<Tuple<string, string, string>>>(reader.GetString(2));
                                }
                                List<Dictionary<string, string>> res = new List<Dictionary<string, string>>();
                                foreach (var tuple in list)
                                {
                                    res.Add(new Dictionary<string, string>
                                    {
                                        { "subject", tuple.Item1 },
                                        { "predicate", tuple.Item2 },
                                        { "object", tuple.Item3 }
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
    }
}
