﻿using Microsoft.Data.Sqlite;
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

        readonly List<Record> records = new List<Record>();

        readonly List<Tuple<string, string>> entityLabels = new List<Tuple<string, string>>();
        readonly List<Tuple<string, string>> predicateLabels = new List<Tuple<string, string>>();

        readonly List<string> hints = new List<string>();
        readonly List<string> stopwords = new List<string>();
        readonly HashSet<string> appearedHints = new HashSet<string>();
        readonly HashSet<string> appearedStopwords = new HashSet<string>();

        public SearchWindow searchWindow = null;

        public DictionaryManager hintsManager = null;
        public DictionaryManager stopwordsManager = null;

        public readonly string MetaName = ".rldb-meta";
        public string metaPath;

        public string filePath;
        public int idx = -1;

        string currentText;

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
            using (var connection = new SqliteConnection($"Data Source={metaPath}"))
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
                foreach (var control in record.Controls)
                {
                    control.LoadLabels();
                }
            }
        }
        
        void CreateDictionaryTable(SqliteConnection connection)
        {
            var command = connection.CreateCommand();
            command.CommandText = @"
                drop table if exists dictionary;
            ";
            command.ExecuteNonQuery();
            command.CommandText = @"
                create table dictionary (
                    name text not null,
                    type int not null
                );
            ";
            command.ExecuteNonQuery();
        }

        List<string> GetWords(SqliteConnection connection, int type)
        {
            // if type == 0, return stopwords,
            // otherwise, return hints
            List<string> words = new List<string>();
            var command = connection.CreateCommand();
            command.CommandText = @"
                select name from dictionary where type = $type;
            ";
            command.Parameters.AddWithValue("$type", type);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    words.Add(reader.GetString(0));
                }
            }
            return words;
        }

        void GetAppearedWords()
        {
            if (idx != -1)
            {
                appearedStopwords.Clear();
                appearedHints.Clear();
                foreach (var word in stopwords)
                {
                    if (currentText.Contains(word))
                    {
                        appearedStopwords.Add(word);
                    }
                }
                foreach (var word in hints)
                {
                    if (currentText.Contains(word))
                    {
                        appearedHints.Add(word);
                    }
                }
            }
        }

        void LoadWords()
        {
            stopwords.Clear();
            hints.Clear();
            using (var connection = new SqliteConnection($"Data Source={metaPath}"))
            {
                connection.Open();
                try
                {
                    stopwords.AddRange(GetWords(connection, 0));
                }
                catch (SqliteException)
                {
                    // assume this is triggered by missing the table
                    CreateDictionaryTable(connection);
                    stopwords.AddRange(GetWords(connection, 0));
                }
                hints.AddRange(GetWords(connection, 1));
            }

            if (stopwordsManager != null)
                stopwordsManager.LoadWords();
            if (hintsManager != null)
                hintsManager.LoadWords();

            GetAppearedWords();
            ReloadText();
        }

        public void AddWord(string word, int type)
        {
            if (type == 0)
            {
                if (stopwords.Contains(word))
                {
                    return;
                }
            }
            else
            {
                if (hints.Contains(word))
                {
                    return;
                }
            }
            using (var connection = new SqliteConnection($"Data Source={metaPath}"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        insert into dictionary (name, type) values ($name, $type);
                    ";
                    command.Parameters.AddWithValue("$name", word);
                    command.Parameters.AddWithValue("$type", type);
                    command.ExecuteNonQuery();
                }
            }
            LoadWords();
        }

        public void RemoveWord(string word, int type)
        {
            if (type == 0)
            {
                if (!stopwords.Contains(word))
                {
                    return;
                }
            }
            else
            {
                if (!hints.Contains(word))
                {
                    return;
                }
            }
            using (var connection = new SqliteConnection($"Data Source={metaPath}"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        delete from dictionary where name = $name and type = $type;
                    ";
                    command.Parameters.AddWithValue("$name", word);
                    command.Parameters.AddWithValue("$type", type);
                    command.ExecuteNonQuery();
                }
            }
            LoadWords();
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
                            "",
                            "",
                            record.SubjectType,
                            null,
                            null));
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

        static void SortEntities(List<string> entities)
        {
            entities.Sort((x, y) => x.Length == y.Length ? x.CompareTo(y) : y.Length - x.Length);
        }

        List<string> GetEntities()
        {
            List<string> entities = new List<string>();
            foreach (var record in records)
            {
                entities.Add(record.Subject);
            }
            SortEntities(entities);
            //MessageBox.Show(String.Join(", ", entities));
            return entities;
        }

        void SetStyle(List<string> words, DependencyProperty property, object value)
        {
            SortEntities(words);
            int last = 0;
            while (true)
            {
                TextPointer position = SentenceText.Document.ContentStart;
                string s = "";
                int idx = 0;
                int end = -1;
                TextPointer startPointer = null;
                bool updated = false;
                while (position != null)
                {
                    bool found = false;
                    if (position.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                    {
                        string textRun = position.GetTextInRun(LogicalDirection.Forward);
                        s += textRun;
                        int start = idx;
                        while (idx < s.Length)
                        {
                            if (idx == end)
                            {
                                TextPointer endPointer = position.GetPositionAtOffset(idx - start);
                                TextRange range = new TextRange(startPointer, endPointer);
                                range.ApplyPropertyValue(property, value);
                                last = end;
                                found = true;
                                break;
                            }
                            if (end == -1 && idx >= last)
                            {
                                foreach (var text in words)
                                {
                                    if (idx + text.Length <= currentText.Length && currentText.Substring(idx, text.Length) == text)
                                    {
                                        startPointer = position.GetPositionAtOffset(idx - start);
                                        end = idx + text.Length;
                                        break;
                                    }
                                }
                            }
                            idx++;
                        }
                    }
                    if (found)
                    {
                        updated = true;
                        break;
                    }
                    position = position.GetNextContextPosition(LogicalDirection.Forward);
                }
                if (!updated)
                {
                    break;
                }
            }
        }

        void SetHints(List<string> words)
        {
            SetStyle(words, Inline.TextDecorationsProperty, TextDecorations.Underline);
        }

        void SetStopwords(List<string> words)
        {
            SetStyle(words, Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
        }

        void GetDocument()
        {
            if (idx == -1) return;
            foreach (var record in records)
            {
                record.Controls.Clear();
            }
            RecordsList.Items.Clear();

            Paragraph paragraph = new Paragraph();

            List<string> entities = GetEntities();

            Color[] c = new Color[] { Colors.Red, Colors.Green, Colors.Blue };
            int j = 0;

            string word = null;
            if (searchWindow != null && searchWindow.SearchText != "")
            {
                word = searchWindow.SearchText;
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
                        var record = records.Find((x) => x.Subject == e);
                        RecordsList.Items.Add(
                            new RecordControl(this, entityLabels, predicateLabels, record));

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
            SentenceText.Document = document;

            // display stopwords & hints

            SetHints(new List<string>(appearedHints));
            SetStopwords(new List<string>(appearedStopwords));
        }

        public void ReloadText()
        {
            double offset = SentenceText.VerticalOffset;
            GetDocument();
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
                        foreach (var tuple in data)
                        {
                            Record record = new Record
                            {
                                Subject = tuple.Item1,
                                SubjectType = tuple.Item4
                            };
                            if (records.Find((x) => x.Subject == record.Subject) == null)
                            {
                                records.Add(record);
                            }
                        }

                        currentText = reader.GetString(1);
                        GetAppearedWords();
                        GetDocument();
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

        public void OpenFile(string fileName, int selectedSentence, bool isLineId = false)
        {
            if (idx != -1)
            {
                SaveCurrentRecords();
            }

            idx = -1;
            records.Clear();
            entityLabels.Clear();
            RecordsList.Items.Clear();
            SelectedSentence.Items.Clear();

            currentText = "";
            SentenceText.Document.Blocks.Clear();

            filePath = fileName;

            FileInfo info = new FileInfo(fileName);
            metaPath = Path.Combine(info.Directory.FullName, MetaName);

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

            ExportButton.IsEnabled = true;
            EntityLabelManagerButton.IsEnabled = true;
            //PredicateLabelManagerButton.IsEnabled = true;

            PreviousSentenceButton.IsEnabled = true;
            SelectedSentence.IsEnabled = true;
            NextSentenceButton.IsEnabled = true;

            SearchButton.IsEnabled = true;
            StopwordsManagerButton.IsEnabled = true;
            HintsManagerButton.IsEnabled = true;

            LoadLabels();

            LoadWords();
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

        void ShowSearch()
        {
            if (searchWindow == null)
                searchWindow = new SearchWindow(this);
            searchWindow.Show();
            searchWindow.Activate();
        }

        void ShowStopwordsManager()
        {
            if (stopwordsManager == null)
                stopwordsManager = new DictionaryManager(this, stopwords, 0);
            stopwordsManager.Show();
            stopwordsManager.Activate();
        }

        void ShowHintsManager()
        {
            if (hintsManager == null)
                hintsManager = new DictionaryManager(this, hints, 1);
            hintsManager.Show();
            hintsManager.Activate();
        }
        
        private void SentenceText_KeyDown(object sender, KeyEventArgs e)
        {
            if (idx == -1) return;
            if (e.Key == Key.E)
            {
                GoPrevious();
            }
            else if (e.Key == Key.R)
            {
                GoNext();
            }
            else if (e.Key == Key.F && Keyboard.IsKeyDown(Key.LeftCtrl))
            {
                ShowSearch();
                if (SentenceText.Selection.Text != "")
                {
                    searchWindow.SearchBox.Text = SentenceText.Selection.Text;
                    searchWindow.Search();
                }
            }
            else if (e.Key == Key.Q && Keyboard.IsKeyDown(Key.LeftCtrl))
            {
                string text = SentenceText.Selection.Text;
                if (text != "")
                {
                    ShowStopwordsManager();
                    if (Keyboard.IsKeyDown(Key.LeftShift)) // remove
                    {
                        RemoveWord(text, 0);
                        stopwordsManager.WordBox.Text = text;
                    }
                    else // add
                    {
                        AddWord(text, 0);
                        stopwordsManager.WordList.SelectedItem = text;
                    }
                }
            }
            else if (e.Key == Key.W && Keyboard.IsKeyDown(Key.LeftCtrl))
            {
                string text = SentenceText.Selection.Text;
                if (text != "")
                {
                    ShowHintsManager();
                    if (Keyboard.IsKeyDown(Key.LeftShift)) // remove
                    {
                        RemoveWord(text, 1);
                        hintsManager.WordBox.Text = text;
                    }
                    else // add
                    {
                        AddWord(text, 1);
                        hintsManager.WordList.SelectedItem = text;
                    }
                }
            }
        }

        List<Tuple<int, int>> FindOccurrences(string text)
        {
            List<Tuple<int, int>> results = new List<Tuple<int, int>>();
            for (int i = 0; i < currentText.Length; i++)
            {
                if (i + text.Length <= currentText.Length && currentText.Substring(i, text.Length) == text)
                {
                    results.Add(new Tuple<int, int>(i, i + text.Length));
                    i += text.Length - 1;
                }
            }
            return results;
        }

        List<Tuple<int, int>> FindOccurrences(List<string> entities)
        {
            List<Tuple<int, int>> results = new List<Tuple<int, int>>();
            for (int i = 0; i < currentText.Length; i++)
            {
                foreach (var e in entities)
                {
                    if (i + e.Length <= currentText.Length && currentText.Substring(i, e.Length) == e)
                    {
                        results.Add(new Tuple<int, int>(i, i + e.Length));
                        i += e.Length - 1;
                        break;
                    }
                }
            }
            return results;
        }

        static bool Intercept(Tuple<int, int> a, Tuple<int, int> b)
        {
            return !(a.Item2 <= b.Item1 || b.Item2 <= a.Item1);
        }

        static int GetOffset(TextPointer position)
        {
            string s = "";
            while (position != null)
            {
                if (position.GetPointerContext(LogicalDirection.Backward) == TextPointerContext.Text)
                {
                    string textRun = position.GetTextInRun(LogicalDirection.Backward);
                    s = textRun + s;
                }
                position = position.GetNextContextPosition(LogicalDirection.Backward);
            }
            return s.Length;
        }

        Tuple<int, int> GetOccurrenceOfSelectedText()
        {
            return new Tuple<int, int>(
                GetOffset(SentenceText.Selection.Start),
                GetOffset(SentenceText.Selection.End));
        }

        private void SentenceText_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (idx == -1) return;
            if (e.Key == Key.Space)
            {
                string selectedText = SentenceText.Selection.Text;

                if (selectedText == "") return;

                Record existedRecord = records.Find((x) => x.Subject == selectedText);
                if (existedRecord != null)
                {
                    records.Remove(existedRecord);
                }
                else
                {
                    Tuple<int, int> targetOccurrence = GetOccurrenceOfSelectedText();

                    List<string> entities = GetEntities();
                    List<Tuple<int, int>> allEntitiesOccurrences = FindOccurrences(entities);
                    Dictionary<string, int> entitiesOccurrences = new Dictionary<string, int>();
                    foreach (var entityOccurrence in allEntitiesOccurrences)
                    {
                        string s = currentText.Substring(entityOccurrence.Item1, entityOccurrence.Item2 - entityOccurrence.Item1);
                        if (entitiesOccurrences.ContainsKey(s))
                        {
                            entitiesOccurrences[s]++;
                        }
                        else
                        {
                            entitiesOccurrences.Add(s, 1);
                        }
                    }

                    if (currentText.Substring(targetOccurrence.Item1, targetOccurrence.Item2 - targetOccurrence.Item1) == selectedText)
                    {
                        foreach (var entityOccurrence in allEntitiesOccurrences)
                        {
                            if (Intercept(targetOccurrence, entityOccurrence))
                            {
                                string s = currentText.Substring(entityOccurrence.Item1, entityOccurrence.Item2 - entityOccurrence.Item1);
                                entitiesOccurrences[s]--;
                                if (entitiesOccurrences[s] == 0)
                                {
                                    entitiesOccurrences.Remove(s);
                                    records.Remove(records.Find((x) => x.Subject == s));
                                }
                            }
                        }
                        records.Add(new Record { Subject = selectedText });

                        // remove redundancies

                        entities = GetEntities();
                        allEntitiesOccurrences = FindOccurrences(entities);
                        entitiesOccurrences = new Dictionary<string, int>();
                        foreach (var entityOccurrence in allEntitiesOccurrences)
                        {
                            string s = currentText.Substring(entityOccurrence.Item1, entityOccurrence.Item2 - entityOccurrence.Item1);
                            if (entitiesOccurrences.ContainsKey(s))
                            {
                                entitiesOccurrences[s]++;
                            }
                            else
                            {
                                entitiesOccurrences.Add(s, 1);
                            }
                        }
                        foreach (var s in entities)
                        {
                            if (!entitiesOccurrences.ContainsKey(s))
                            {
                                records.Remove(records.Find((x) => x.Subject == s));
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show(
                            "An error has occurred! Please report the incident.",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);

                        List<Tuple<int, int>> selectedTextOccurrences = FindOccurrences(selectedText);
                        foreach (var occurrence in selectedTextOccurrences)
                        {
                            List<Tuple<int, int>> allEntitiesOccurrences2 = new List<Tuple<int, int>>();
                            foreach (var entityOccurrence in allEntitiesOccurrences)
                            {
                                if (Intercept(occurrence, entityOccurrence))
                                {
                                    string s = currentText.Substring(entityOccurrence.Item1, entityOccurrence.Item2 - entityOccurrence.Item1);
                                    entitiesOccurrences[s]--;
                                    if (entitiesOccurrences[s] == 0)
                                    {
                                        entitiesOccurrences.Remove(s);
                                        records.Remove(records.Find((x) => x.Subject == s));
                                    }
                                }
                                else
                                {
                                    allEntitiesOccurrences2.Add(entityOccurrence);
                                }
                            }
                            allEntitiesOccurrences = allEntitiesOccurrences2;
                        }
                        records.Add(new Record { Subject = selectedText });
                    }
                }

                ReloadText();
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
            if (stopwordsManager != null) stopwordsManager.Close();
            if (hintsManager != null) hintsManager.Close();
            if (idx != -1)
            {
                SaveCurrentRecords();
            }
        }

        private void EntityLabelManagerButton_Click(object sender, RoutedEventArgs e)
        {
            new LabelManager(metaPath, entityLabels, 0).ShowDialog();
            LoadLabels();
        }

        private void PredicateLabelManagerButton_Click(object sender, RoutedEventArgs e)
        {
            new LabelManager(metaPath, predicateLabels, 1).ShowDialog();
            LoadLabels();
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSearch();
        }

        private void StopwordsManagerButton_Click(object sender, RoutedEventArgs e)
        {
            ShowStopwordsManager();
        }

        private void HintsManagerButton_Click(object sender, RoutedEventArgs e)
        {
            ShowHintsManager();
        }
    }
}
