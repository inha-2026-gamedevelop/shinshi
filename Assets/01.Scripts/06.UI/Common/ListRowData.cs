// System
using System;
using System.Collections.Generic;

namespace Shinshi.UI
{
    /// <summary> 목록 한 줄에 보일 내용, 글자 칸 값과 등급 </summary>
    public readonly struct ListRowData
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string[] _columns;
        private readonly int      _grade;

        /****************************************
        *              Properties
        ****************************************/

        public string[] Columns => _columns ?? Array.Empty<string>();
        public int      Grade   => _grade;

        /****************************************
        *                Methods
        ****************************************/

        public ListRowData(int grade, params string[] columns)
        {
            _grade   = grade;
            _columns = columns;
        }

        /// <summary> 줄 배열을 데이터로 채운다. 데이터보다 많은 줄은 숨긴다. 선택 줄 번호가 -1이면 선택 표시가 없다 </summary>
        public static void Fill(ListRowView[] rows, IReadOnlyList<ListRowData> data, int selectedIndex)
        {
            if (rows == null)
            {
                return;
            }

            int count = (data != null) ? data.Count : 0;
            for (int i = 0; i < rows.Length; ++i)
            {
                ListRowView row = rows[i];
                if (row == null)
                {
                    continue;
                }

                row.Index = i;
                if (i >= count)
                {
                    row.SetVisible(false);
                    continue;
                }

                row.SetVisible(true);
                row.SetColumns(data[i].Columns);
                row.SetGrade(data[i].Grade);
                row.SetSelected(i == selectedIndex);
            }
        }
    }
}
