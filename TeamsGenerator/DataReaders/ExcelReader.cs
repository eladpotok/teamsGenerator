using System;
using System.Collections.Generic;
using System.Data.OleDb;
using TeamsGenerator.Algos.SkillWiseAlgo;

namespace TeamsGenerator.DataReaders
{
    class ExcelReader : IDataReader<IEnumerable<SkillWisePlayer>> 
    {
        private readonly string _filePath;
        private readonly string _sheetName;

        public ExcelReader(string filePath, string sheetName)
        {
            _filePath = filePath;
            _sheetName = sheetName;
        }

        public IEnumerable<SkillWisePlayer> Read()
        {
            var result = new List<SkillWisePlayer>();
            try
            {
                var connectionString = "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" + _filePath + ";Extended Properties=Excel 12.0;";
                using (OleDbConnection connection = new OleDbConnection(connectionString))
                {
                    connection.Open();
                    OleDbCommand command = new OleDbCommand($"select * from [{_sheetName}$]", connection);
                    using (OleDbDataReader row = command.ExecuteReader())
                    {
                        while (row.Read())
                        {
                            var isArrive = row[2].ToString() != "";

                            if (isArrive)
                            {
                                AddPlayer(result, row);
                            }
                        }
                    }
                }

                return result;
            }
            catch (Exception e)
            {
                return null;
            }

        }

        private static void AddPlayer(List<SkillWisePlayer> result, OleDbDataReader row)
        {
            var name = row[0].ToString();
            var rank = float.Parse(row[1].ToString());

            int.TryParse(row[3].ToString(), out int attack);
            int.TryParse(row[4].ToString(), out int defence);
            int.TryParse(row[5].ToString(), out int stamina);
            int.TryParse(row[6].ToString(), out int leadership);
            int.TryParse(row[7].ToString(), out int passing);
            result.Add(new SkillWisePlayer() { Name = name, Rank = rank, Attack = attack, Defence = defence, Stamina = stamina, Leadership = leadership, Passing = passing });
        }
    }
}
