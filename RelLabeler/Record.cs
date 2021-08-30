using System.Collections.Generic;

namespace RelLabeler
{
    public class Record
    {
        private string subject = "";
        private string subjectType = "";

        public List<RecordControl> Controls = new List<RecordControl>();

        bool isSubjectFlag = false;
        public string Subject
        {
            get { return subject; }
            set
            {
                if (isSubjectFlag) return;
                isSubjectFlag = true;
                subject = value;
                foreach (var control in Controls)
                {
                    control.SubjectText.Text = value;
                }
                isSubjectFlag = false;
            }
        }

        bool isSubjectTypeFlag = false;
        public string SubjectType
        {
            get { return subjectType; }
            set
            {
                if (isSubjectTypeFlag) return;
                isSubjectTypeFlag = true;
                subjectType = value;
                foreach (var control in Controls)
                {
                    control.SelectSubjectLabel(value);
                }
                if (Controls.Count > 0)
                {
                    Controls[0].mainWindow.AddOrUpdateCache(subject, value);
                }
                isSubjectTypeFlag = false;
            }
        }
    }
}
