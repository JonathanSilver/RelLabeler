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

        readonly List<Record> records = new List<Record>();

        readonly List<Tuple<string, string>> entityLabels = new List<Tuple<string, string>>();
        readonly List<Tuple<string, string>> predicateLabels = new List<Tuple<string, string>>();

        readonly Dictionary<string, string> cache = new Dictionary<string, string>();

        readonly List<string> hints = new List<string>();
        readonly List<string> stopwords = new List<string>();
        readonly HashSet<string> appearedHints = new HashSet<string>();
        readonly HashSet<string> appearedStopwords = new HashSet<string>();

        public SearchWindow searchWindow = null;

        public DictionaryManager hintsManager = null;
        public DictionaryManager stopwordsManager = null;

        public LabelManager entityLabelManager = null;

        public readonly string MetaName = ".rldb-meta";
        public string metaPath;

        public string filePath;
        public int idx = -1;

        string currentText;

        public bool showAnnotations = false;

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

        public void LoadLabels()
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
            if (entityLabelManager != null) entityLabelManager.LoadLabels();
        }

        void CreateCacheTable(SqliteConnection connection)
        {
            var command = connection.CreateCommand();
            command.CommandText = @"
                drop table if exists cache;
            ";
            command.ExecuteNonQuery();
            command.CommandText = @"
                create table cache (
                    entity text not null unique,
                    type text not null
                );
            ";
            command.ExecuteNonQuery();
        }

        void GetCache(SqliteConnection connection)
        {
            cache.Clear();
            var command = connection.CreateCommand();
            command.CommandText = @"
                select entity, type from cache;
            ";
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    cache.Add(reader.GetString(0), reader.GetString(1));
                }
            }
        }

        void LoadCache() // this should NOT be called on a regular basis
        {
            using (var connection = new SqliteConnection($"Data Source={metaPath}"))
            {
                connection.Open();
                try
                {
                    GetCache(connection);
                }
                catch (SqliteException)
                {
                    // assume this is triggered by missing the table
                    CreateCacheTable(connection);
                    GetCache(connection);
                }
            }
        }

        public void AddOrUpdateCache(string entity, string type)
        {
            if (type == null) type = "";
            if (cache.ContainsKey(entity) && cache[entity] == type) return;
            using (var connection = new SqliteConnection($"Data Source={metaPath}"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    if (cache.ContainsKey(entity))
                    {
                        command.CommandText = @"
                            update cache set type = $type where entity = $entity;
                        ";
                    }
                    else
                    {
                        command.CommandText = @"
                            insert into cache (entity, type) values ($entity, $type);
                        ";
                    }
                    command.Parameters.AddWithValue("$entity", entity);
                    command.Parameters.AddWithValue("$type", type);
                    command.ExecuteNonQuery();
                }
            }
            cache[entity] = type;
        }

        void RemoveCache(string entity)
        {
            if (!cache.ContainsKey(entity)) return;
            using (var connection = new SqliteConnection($"Data Source={metaPath}"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        delete from cache where entity = $entity;
                    ";
                    command.Parameters.AddWithValue("$entity", entity);
                    command.ExecuteNonQuery();
                }
            }
            cache.Remove(entity);
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
                foreach (var word in cache)
                {
                    if (currentText.Contains(word.Key))
                    {
                        appearedHints.Add(word.Key);
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
                List<DataRecord> data
                    = new List<DataRecord>();
                foreach (var record in records)
                {
                    // the order matters!
                    // for forward compatibility
                    data.Add(
                        new DataRecord
                        {
                            Item1 = record.Subject,
                            Item4 = record.SubjectType,
                            Item7 = record.Position,
                            Item8 = record.Annotated
                        });
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

        TextPointer GetTextPointer(int pos)
        {
            string s = "";
            TextPointer position = SentenceText.Document.ContentStart;
            while (position != null)
            {
                if (position.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    int start = s.Length;
                    string textRun = position.GetTextInRun(LogicalDirection.Forward);
                    s += textRun;
                    if (s == currentText)
                    {
                        s += " ";
                    }
                    if (s.Length > pos)
                    {
                        return position.GetPositionAtOffset(pos - start);
                    }
                }
                position = position.GetNextContextPosition(LogicalDirection.Forward);
            }
            return null;
        }

        Tuple<TextPointer, TextPointer> GetRange(Tuple<int, int> pos)
        {
            if (pos == null) return null;
            return new Tuple<TextPointer, TextPointer>(
                GetTextPointer(pos.Item1),
                GetTextPointer(pos.Item2));
        }

        void SetStyle(Tuple<int, int> pos, Tuple<DependencyProperty, object>[] styles)
        {
            if (pos == null) return;
            var range = GetRange(pos);
            if (range.Item1 != null && range.Item2 != null)
            {
                TextRange textRange = new TextRange(range.Item1, range.Item2);
                foreach (var tuple in styles)
                {
                    DependencyProperty property = tuple.Item1;
                    object value = tuple.Item2;
                    textRange.ApplyPropertyValue(property, value);
                }
            }
        }

        void GetDocument()
        {
            if (idx == -1) return;

            FlowDocument document = new FlowDocument();
            Paragraph paragraph = new Paragraph();
            Run run = new Run(currentText);
            paragraph.Inlines.Add(run);
            document.Blocks.Add(paragraph);
            SentenceText.Document = document;

            // display entities

            foreach (var record in records)
            {
                record.Controls.Clear();
            }
            RecordsList.Items.Clear();

            List<Tuple<int, int>> allEntityOccurrences = new List<Tuple<int, int>>();
            Brush[] brushes = new Brush[] { Brushes.Blue, Brushes.Green };
            int j = 0;
            List<Record> entityRecords = records.FindAll((x) => !x.Annotated);
            entityRecords.Sort((x, y)
                => x.Position.Item1 == y.Position.Item1
                ? x.Position.Item2 - y.Position.Item2
                : x.Position.Item1 - y.Position.Item1);
            foreach (var record in entityRecords)
            {
                Tuple<int, int> pos = record.Position;
                SetStyle(pos, new Tuple<DependencyProperty, object>[]
                {
                    new Tuple<DependencyProperty, object>(
                        TextElement.FontWeightProperty, FontWeights.Bold),
                    new Tuple<DependencyProperty, object>(
                        TextElement.ForegroundProperty, brushes[(j++) % brushes.Length])
                });
                allEntityOccurrences.Add(pos);
                RecordsList.Items.Add(
                    new RecordControl(this, entityLabels, predicateLabels, record));
            }

            // display hints
            List<Tuple<int, int>> hintOccurrences = FindOccurrences(new List<string>(appearedHints), false, allEntityOccurrences);
            foreach (var occurrence in hintOccurrences)
            {
                SetStyle(occurrence, new Tuple<DependencyProperty, object>[]
                {
                    new Tuple<DependencyProperty, object>(
                        Inline.TextDecorationsProperty, TextDecorations.Underline),
                    new Tuple<DependencyProperty, object>(
                        TextElement.BackgroundProperty, Brushes.LightCyan)
                });
            }

            // display stopwords
            List<Tuple<int, int>> stopwordOccurrences = FindOccurrences(new List<string>(appearedStopwords));
            foreach (var occurrence in stopwordOccurrences)
            {
                SetStyle(occurrence, new Tuple<DependencyProperty, object>[]
                {
                    new Tuple<DependencyProperty, object>(
                        Inline.TextDecorationsProperty, TextDecorations.Strikethrough),
                    new Tuple<DependencyProperty, object>(
                        TextElement.BackgroundProperty, Brushes.LightGray)
                });
            }

            if (showAnnotations)
            {
                List<Record> annotatedRecords = records.FindAll((x) => x.Annotated);
                Dictionary<string, Record> annotatedDict = new Dictionary<string, Record>();
                foreach (var record in annotatedRecords)
                {
                    if (!annotatedDict.ContainsKey(record.Subject.ToLower()))
                    {
                        annotatedDict[record.Subject.ToLower()] = record;
                    }
                }
                HashSet<string> occurredAnnotations = new HashSet<string>();
                List<Tuple<int, int>> annotationOccurrences = FindOccurrences(annotatedRecords.ConvertAll((x) => x.Subject), true);
                foreach (var occurrence in annotationOccurrences)
                {
                    SetStyle(occurrence, new Tuple<DependencyProperty, object>[]
                    {
                        new Tuple<DependencyProperty, object>(
                            TextElement.BackgroundProperty, Brushes.Orange)
                    });
                    string text = currentText.Substring(occurrence.Item1, occurrence.Item2 - occurrence.Item1).ToLower();
                    RecordControl control = new RecordControl(this, entityLabels, predicateLabels, annotatedDict[text]);
                    control.SetAsAnnotation();
                    RecordsList.Items.Add(control);
                    occurredAnnotations.Add(text);
                }
                foreach (var entry in annotatedDict)
                {
                    if (!occurredAnnotations.Contains(entry.Key))
                    {
                        RecordControl control = new RecordControl(this, entityLabels, predicateLabels, entry.Value);
                        control.SetAsInvisible();
                        RecordsList.Items.Add(control);
                    }
                }
            }

            // display matched search text

            if (searchWindow != null && searchWindow.SearchText != "")
            {
                List<Tuple<int, int>> searchTextOccurrences = FindOccurrences(searchWindow.SearchText, true);
                foreach (var occurrence in searchTextOccurrences)
                {
                    SetStyle(occurrence, new Tuple<DependencyProperty, object>[]
                    {
                        new Tuple<DependencyProperty, object>(
                            TextElement.BackgroundProperty, Brushes.Yellow)
                    });
                }
            }
        }

        void SelectText(Tuple<int, int> pos)
        {
            if (pos == null) return;
            var range = GetRange(pos);
            if (range.Item1 != null && range.Item2 != null)
            {
                SentenceText.Selection.Select(range.Item1, range.Item2);
            }
        }

        List<Tuple<int, int>> FindOccurrences(string text, bool ignoreCase = false, List<Tuple<int, int>> excludePositions = null)
        {
            List<Tuple<int, int>> results = new List<Tuple<int, int>>();
            for (int i = 0; i < currentText.Length; i++)
            {
                if (i + text.Length <= currentText.Length
                    && (ignoreCase ? currentText.Substring(i, text.Length).ToLower() == text.ToLower()
                        : currentText.Substring(i, text.Length) == text))
                {
                    Tuple<int, int> targetOccurrence = new Tuple<int, int>(i, i + text.Length);
                    if (excludePositions == null
                        || excludePositions.Find((x) => Intercept(targetOccurrence, x)) == null)
                    {
                        results.Add(targetOccurrence);
                        i += text.Length - 1;
                    }
                }
            }
            return results;
        }

        List<Tuple<int, int>> FindOccurrences(List<string> entities, bool ignoreCase = false, List<Tuple<int, int>> excludePositions = null)
        {
            SortEntities(entities);
            List<Tuple<int, int>> results = new List<Tuple<int, int>>();
            for (int i = 0; i < currentText.Length; i++)
            {
                foreach (var e in entities)
                {
                    if (i + e.Length <= currentText.Length
                        && (ignoreCase ? currentText.Substring(i, e.Length).ToLower() == e.ToLower()
                            : currentText.Substring(i, e.Length) == e))
                    {
                        Tuple<int, int> targetOccurrence = new Tuple<int, int>(i, i + e.Length);
                        if (excludePositions == null
                            || excludePositions.Find((x) => Intercept(targetOccurrence, x)) == null)
                        {
                            results.Add(targetOccurrence);
                            i += e.Length - 1;
                            break;
                        }
                    }
                }
            }
            return results;
        }

        public void SelectFirstMatchedText(string text)
        {
            if (text != "")
            {
                var occurrences = FindOccurrences(text, true);
                if (occurrences.Count > 0)
                {
                    SelectText(occurrences[0]);
                }
            }
        }

        public void ReloadText()
        {
            Tuple<int, int> selectedPos = null;
            if (SentenceText.Selection.Text != "")
            {
                selectedPos = GetOccurrenceOfSelectedText();
            }

            double offset = SentenceText.VerticalOffset;
            GetDocument();
            SentenceText.ScrollToVerticalOffset(offset);

            SelectText(selectedPos);
        }

        void SelectSentence(int idx, bool save = true)
        {
            if (idx == -1) return;
            if (save) SaveCurrentRecords();
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
                        currentText = reader.GetString(1);

                        List<DataRecord> data;
                        if (reader.GetString(2) == "")
                        {
                            data = new List<DataRecord>();
                        }
                        else
                        {
                            data = JsonSerializer.Deserialize<
                                List<DataRecord>
                                >(reader.GetString(2));
                        }

                        bool missingPositions = false;
                        foreach (var tuple in data)
                        {
                            if (tuple.Item7 == null && tuple.Item8 == null)
                            {
                                missingPositions = true;
                                break;
                            }
                        }
                        records.Clear();
                        if (missingPositions)
                        {
                            Dictionary<string, string> entityType = new Dictionary<string, string>();
                            foreach (var tuple in data.FindAll((x) => !x.Item8.GetValueOrDefault(false)))
                            {
                                entityType[tuple.Item1] = tuple.Item4;
                            }
                            List<Tuple<int, int>> occurrences = FindOccurrences(new List<string>(entityType.Keys));
                            foreach (var pos in occurrences)
                            {
                                string subject = currentText.Substring(pos.Item1, pos.Item2 - pos.Item1);
                                if (entityType.ContainsKey(subject))
                                {
                                    Record record = new Record
                                    {
                                        Subject = subject,
                                        SubjectType = entityType[subject],
                                        Position = pos
                                    };
                                    records.Add(record);
                                }
                            }
                            foreach (var tuple in data.FindAll((x) => x.Item8.GetValueOrDefault(false)))
                            {
                                Record record = new Record
                                {
                                    Subject = tuple.Item1,
                                    SubjectType = tuple.Item4,
                                    Annotated = true
                                };
                                records.Add(record);
                            }
                        }
                        else
                        {
                            foreach (var tuple in data)
                            {
                                Record record = new Record
                                {
                                    Subject = tuple.Item1,
                                    SubjectType = tuple.Item4,
                                    Position = tuple.Item7,
                                    Annotated = tuple.Item8.GetValueOrDefault(false)
                                };
                                if (record.Annotated || !record.Annotated
                                    && currentText.Substring(record.Position.Item1, record.Position.Item2 - record.Position.Item1) == record.Subject)
                                {
                                    records.Add(record);
                                }
                            }
                        }

                        GetAppearedWords();
                        GetDocument();
                    }
                    if (reader.Read())
                    {
                        throw new InvalidDataException("Duplicated Sentences!");
                    }
                }
            }

            UpdatePreviousNextButtonStatus();
        }

        void UpdatePreviousNextButtonStatus()
        {
            if (idx == -1)
            {
                PreviousSentenceButton.IsEnabled = false;
                NextSentenceButton.IsEnabled = false;
            }
            else
            {
                PreviousSentenceButton.IsEnabled = idx > 0;
                NextSentenceButton.IsEnabled = idx + 1 < SelectedSentence.Items.Count;
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

            PreviousSentenceButton.IsEnabled = false;
            NextSentenceButton.IsEnabled = false;

            currentText = "";
            SentenceText.Document.Blocks.Clear();

            filePath = fileName;

            FileInfo info = new FileInfo(fileName);
            metaPath = Path.Combine(info.Directory.FullName, MetaName);

            LoadCache();

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

            ImportButton.IsEnabled = true;
            ExportButton.IsEnabled = true;
            EntityLabelManagerButton.IsEnabled = true;
            //PredicateLabelManagerButton.IsEnabled = true;

            SelectedSentence.IsEnabled = true;

            SearchButton.IsEnabled = true;
            StopwordsManagerButton.IsEnabled = true;
            HintsManagerButton.IsEnabled = true;

            ShowAnnotationsButton.IsEnabled = true;

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
            else if (e.Key == Key.D && Keyboard.IsKeyDown(Key.LeftCtrl) && Keyboard.IsKeyDown(Key.LeftShift))
            {
                string text = SentenceText.Selection.Text;
                if (text != "")
                {
                    RemoveCache(text);
                    GetAppearedWords();
                    ReloadText();
                }
            }
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

                Tuple<int, int> targetOccurrence = GetOccurrenceOfSelectedText();

                if (currentText.Substring(targetOccurrence.Item1, targetOccurrence.Item2 - targetOccurrence.Item1) == selectedText)
                {
                    if (records.RemoveAll((x) => !x.Annotated && targetOccurrence.Equals(x.Position)) == 0)
                    {
                        records.RemoveAll((x) => !x.Annotated && Intercept(targetOccurrence, x.Position));
                        if (cache.ContainsKey(selectedText))
                        {
                            records.Add(new Record
                            {
                                Subject = selectedText,
                                SubjectType = cache[selectedText],
                                Position = targetOccurrence
                            });
                        }
                        else
                        {
                            records.Add(new Record
                            {
                                Subject = selectedText,
                                Position = targetOccurrence
                            });
                            AddOrUpdateCache(selectedText, "");
                            GetAppearedWords();
                        }
                        List<Tuple<int, int>> allEntityOccurrences = new List<Tuple<int, int>>();
                        foreach (var record in records.FindAll((x) => !x.Annotated))
                        {
                            allEntityOccurrences.Add(record.Position);
                        }
                        if (!Keyboard.IsKeyDown(Key.LeftCtrl))
                        {
                            List<Tuple<int, int>> targetOccurrences = FindOccurrences(selectedText, false, allEntityOccurrences);
                            foreach (var occurrence in targetOccurrences)
                            {
                                records.Add(new Record
                                {
                                    Subject = selectedText,
                                    SubjectType = cache[selectedText],
                                    Position = occurrence
                                });
                            }
                        }
                    }
                    else
                    {
                        if (!Keyboard.IsKeyDown(Key.LeftCtrl))
                        {
                            records.RemoveAll((x) => !x.Annotated && x.Subject == selectedText);
                        }
                    }
                }
                //else
                //{
                //    MessageBox.Show(
                //        "An error has occurred! Please report the incident.",
                //        "Error",
                //        MessageBoxButton.OK,
                //        MessageBoxImage.Error);
                //}

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
            if (idx != -1)
            {
                SaveCurrentRecords();
            }
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
                    FileInfo fileInfo = new FileInfo(filePath);
                    foreach (var f in fileInfo.Directory.GetFiles("*.db"))
                    {
                        using (var connection = new SqliteConnection($"Data Source={f.FullName}"))
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
                                    List<DataRecord> list;
                                    if (reader.GetString(2) == "")
                                    {
                                        list = new List<DataRecord>();
                                    }
                                    else
                                    {
                                        list = JsonSerializer.Deserialize<
                                            List<DataRecord>
                                            >(reader.GetString(2));
                                    }
                                    List<Entity> entities = new List<Entity>();
                                    foreach (var tuple in list)
                                    {
                                        if (showAnnotations || !tuple.Item8.GetValueOrDefault(false))
                                        {
                                            entities.Add(new Entity
                                            {
                                                Name = tuple.Item1,
                                                Type = tuple.Item4
                                            });
                                        }
                                    }
                                    Annotation annotation = new Annotation
                                    {
                                        FileName = f.Name,
                                        LineID = reader.GetInt32(0),
                                        Text = reader.GetString(1),
                                        Entities = entities
                                    };
                                    writer.WriteLine(JsonSerializer.Serialize(annotation,
                                        new JsonSerializerOptions
                                        {
                                            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
                                        }));
                                }
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
            if (entityLabelManager != null) entityLabelManager.Close();
            if (idx != -1)
            {
                SaveCurrentRecords();
            }
        }

        private void EntityLabelManagerButton_Click(object sender, RoutedEventArgs e)
        {
            if (entityLabelManager == null)
            {
                entityLabelManager = new LabelManager(this, metaPath, entityLabels, 0);
            }
            entityLabelManager.Show();
            entityLabelManager.Activate();
            //LoadLabels();
        }

        //private void PredicateLabelManagerButton_Click(object sender, RoutedEventArgs e)
        //{
        //    new LabelManager(metaPath, predicateLabels, 1).ShowDialog();
        //    LoadLabels();
        //}

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

        private void SentenceText_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (SentenceText.Selection.Text != "")
            {
                Clipboard.SetText(SentenceText.Selection.Text);
            }
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (idx != -1)
            {
                SaveCurrentRecords();
            }
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Json files (*.json)|*.json|All files (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };
            if (openFileDialog.ShowDialog() == true)
            {
                Dictionary<string, List<Annotation>> annotationDict = new Dictionary<string, List<Annotation>>();
                try
                {
                    using (StreamReader reader = new StreamReader(openFileDialog.OpenFile()))
                    {
                        while (reader.Peek() != -1)
                        {
                            string text = reader.ReadLine();
                            if (text != "")
                            {
                                Annotation annotation = JsonSerializer.Deserialize<Annotation>(text);
                                if (annotation.FileName != null && annotation.Entities != null)
                                {
                                    if (!annotationDict.ContainsKey(annotation.FileName))
                                    {
                                        annotationDict[annotation.FileName] = new List<Annotation>();
                                    }
                                    annotationDict[annotation.FileName].Add(annotation);
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    MessageBox.Show(
                        "Import failed. Check file format.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
                FileInfo fileInfo = new FileInfo(filePath);
                string dirName = fileInfo.Directory.FullName;
                List<string> existedFileNames = new List<string>();
                Dictionary<string, string> mapFullName = new Dictionary<string, string>();
                foreach (var file in annotationDict.Keys)
                {
                    string name = Path.Combine(dirName, file);
                    if (File.Exists(name))
                    {
                        existedFileNames.Add(file);
                        mapFullName[file] = name;
                    }
                }
                existedFileNames.Sort();
                foreach (var file in existedFileNames)
                {
                    string name = mapFullName[file];
                    List<Annotation> annotations = annotationDict[file];
                    annotations.Sort((x, y) => x.LineID - y.LineID);
                    int annotationPointer = 0;
                    using (var connection = new SqliteConnection($"Data Source={name}"))
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
                                int lineID = reader.GetInt32(0);
                                while (annotationPointer < annotations.Count && annotations[annotationPointer].LineID < lineID)
                                {
                                    annotationPointer++;
                                }
                                if (annotationPointer >= annotations.Count)
                                {
                                    break;
                                }
                                if (annotations[annotationPointer].LineID != lineID)
                                {
                                    continue;
                                }
                                List<DataRecord> list;
                                if (reader.GetString(2) == "")
                                {
                                    list = new List<DataRecord>();
                                }
                                else
                                {
                                    list = JsonSerializer.Deserialize<
                                        List<DataRecord>
                                        >(reader.GetString(2));
                                }
                                list = list.FindAll((x) => !x.Item8.GetValueOrDefault(false));
                                foreach (var entity in annotations[annotationPointer].Entities)
                                {
                                    list.Add(new DataRecord
                                    {
                                        Item1 = entity.Name,
                                        Item4 = entity.Type,
                                        Item8 = true
                                    });
                                }
                                var command2 = connection.CreateCommand();
                                command2.CommandText = @"
                                    update data set relations = $data where line_id = $line_id;
                                ";
                                command2.Parameters.AddWithValue("$data",
                                    JsonSerializer.Serialize(list,
                                    new JsonSerializerOptions
                                    {
                                        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
                                    }));
                                command2.Parameters.AddWithValue("$line_id", lineID);
                                command2.ExecuteNonQuery();
                            }
                        }
                    }
                }
                SelectSentence(idx, false);
            }
        }

        private void ShowAnnotationsButton_Checked(object sender, RoutedEventArgs e)
        {
            showAnnotations = !showAnnotations;
            ReloadText();
        }
    }
}
