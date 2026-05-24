using SqlMcp.Tools.Security;

namespace SqlMcp.Tools.Tests;

public class SqlTokenizerTests
{
    [Fact]
    public void HasMultipleStatements_TrailingSemicolon_IsFalse()
    {
        var sql = "SELECT 1;   \n";
        Assert.False(SqlTokenizer.HasMultipleStatements(sql));
    }

    [Fact]
    public void HasMultipleStatements_TwoStatements_IsTrue()
    {
        var sql = "SELECT 1; SELECT 2";
        Assert.True(SqlTokenizer.HasMultipleStatements(sql));
    }

    [Fact]
    public void HasMultipleStatements_SemicolonInString_IsFalse()
    {
        var sql = "SELECT ';' AS x";
        Assert.False(SqlTokenizer.HasMultipleStatements(sql));
    }

    [Fact]
    public void HasMultipleStatements_SemicolonInBlockComment_IsFalse()
    {
        var sql = "SELECT 1 /*;*/";
        Assert.False(SqlTokenizer.HasMultipleStatements(sql));
    }

    [Fact]
    public void Classify_WithCteSelect_IsSelect()
    {
        var c = new SqlStatementClassifier();
        Assert.Equal(SqlStatementType.Select, c.Classify("WITH x AS (SELECT 1) SELECT * FROM x"));
    }

    [Fact]
    public void Classify_DropDatabase_IsDropDatabase()
    {
        var c = new SqlStatementClassifier();
        Assert.Equal(SqlStatementType.DropDatabase, c.Classify("DROP DATABASE test"));
    }
}
