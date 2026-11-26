using System;
using System.ComponentModel;
using System.Windows.Media;

namespace XxtWidget
{
    public class CourseRef
    {
        public string CourseId { get; set; }
        public string ClassId { get; set; }
        public string Cpi { get; set; }
        public string CourseName { get; set; }
        public string ClassName { get; set; }
    }

    public class HomeworkItem : INotifyPropertyChanged
    {
        private static readonly Brush RedBrush = Freeze(0xFF, 0x6B, 0x6B);
        private static readonly Brush OrangeBrush = Freeze(0xFF, 0xB8, 0x6B);
        private static readonly Brush AmberBrush = Freeze(0xFF, 0xD1, 0x66);
        private static readonly Brush BlueBrush = Freeze(0x7E, 0xA2, 0xFF);
        private static readonly Brush GrayBrush = Freeze(0x7A, 0x84, 0x96);

        public string Key { get; set; }
        public string Title { get; set; }
        public string CourseId { get; set; }
        public string ClassId { get; set; }
        public string Cpi { get; set; }
        public string TaskRefId { get; set; }
        public string CourseName { get; set; }
        public string ClassName { get; set; }
        public string DetailUrl { get; set; }
        public string OpenUrl { get; set; }
        public string Status { get; set; }
        public double PcRemainingHours { get; set; }
        public DateTime? PublishDate { get; set; }
        public DateTime? Deadline { get; set; }
        public int Rank { get; set; }

        public HomeworkItem()
        {
            Key = ""; Title = ""; CourseId = ""; ClassId = ""; Cpi = ""; TaskRefId = ""; CourseName = ""; ClassName = ""; DetailUrl = ""; OpenUrl = ""; Status = "1";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public double HoursLeft
        {
            get { return Deadline.HasValue ? (Deadline.Value - DateTime.Now).TotalHours : double.MaxValue; }
        }

        public bool IsOverdue
        {
            get { return Deadline.HasValue && Deadline.Value < DateTime.Now; }
        }

        public string TimeText
        {
            get
            {
                if (!Deadline.HasValue) return "无截止";
                double h = HoursLeft;
                if (h <= 0) return "已截止";
                if (h < 100) return h.ToString("0.0") + " 小时";
                return Math.Round(h).ToString("0") + " 小时";
            }
        }

        public string SubText
        {
            get
            {
                string s = string.IsNullOrEmpty(CourseName) ? ClassName : CourseName;
                if (Status == "3") s = s + " · 待批阅";
                if (Deadline.HasValue) s = s + " · " + Deadline.Value.ToString("MM-dd HH:mm") + " 截止";
                return s;
            }
        }

        public string RankText
        {
            get { return Rank.ToString("00"); }
        }

        public Brush AccentBrush
        {
            get
            {
                if (!Deadline.HasValue) return GrayBrush;
                double h = HoursLeft;
                if (h <= 0) return RedBrush;
                if (h < 6) return RedBrush;
                if (h < 24) return OrangeBrush;
                if (h < 72) return AmberBrush;
                return BlueBrush;
            }
        }

        public void RefreshComputed()
        {
            OnPropertyChanged("TimeText");
            OnPropertyChanged("SubText");
            OnPropertyChanged("AccentBrush");
            OnPropertyChanged("RankText");
        }

        private void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }

        private static Brush Freeze(byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}