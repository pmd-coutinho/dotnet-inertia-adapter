using Microsoft.Data.Sqlite;

namespace KitchenSink;

public sealed record Contact(int Id, string Name, string Email, string Team);
public sealed record ContactPage(Contact[] Data, int CurrentPage, int? NextPage, int Total);

// A real, file-backed database with parameterized queries. Each operation owns its connection.
public sealed class ContactRepository(IConfiguration configuration, IWebHostEnvironment environment)
{
    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(configuration["DATABASE_PATH"] ?? Path.Combine(environment.ContentRootPath, "App_Data", "kitchen-sink.db")),
    }.ToString();

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(new SqliteConnectionStringBuilder(ConnectionString).DataSource)!);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "CREATE TABLE IF NOT EXISTS contacts (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, email TEXT NOT NULL, team TEXT NOT NULL)";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT COUNT(*) FROM contacts";
        if ((long)command.ExecuteScalar()! == 0)
        {
            string[] names = ["Amelia Brooks", "Noah Silva", "Olivia Chen", "Leo Martins", "Isla Morgan", "Oliver Costa", "Mia Patel", "Lucas Reed", "Ava Santos", "Ethan Kim", "Sofia Garcia", "Theo Wilson", "Freya Taylor", "Arthur Lopes", "Lily Hughes", "Oscar King", "Clara Evans", "Finn Scott", "Ella Green", "Hugo Adams", "Alice Baker", "Felix Hall", "Ruby Clark", "James Lewis"];
            for (var index = 0; index < names.Length; index++)
            {
                command.CommandText = "INSERT INTO contacts (name, email, team) VALUES ($name, $email, $team)";
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$name", names[index]);
                command.Parameters.AddWithValue("$email", names[index].ToLowerInvariant().Replace(' ', '.') + "@example.com");
                command.Parameters.AddWithValue("$team", new[] { "Design", "Engineering", "Operations" }[index % 3]);
                command.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }

    public ContactPage Page(string search = "", int page = 1)
    {
        page = Math.Clamp(page, 1, 10000);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM contacts WHERE name LIKE $search OR email LIKE $search";
        command.Parameters.AddWithValue("$search", $"%{search}%");
        var total = Convert.ToInt32(command.ExecuteScalar());
        command.CommandText = "SELECT id, name, email, team FROM contacts WHERE name LIKE $search OR email LIKE $search ORDER BY id DESC LIMIT 6 OFFSET $offset";
        command.Parameters.AddWithValue("$offset", (page - 1) * 6);
        using var reader = command.ExecuteReader();
        var contacts = new List<Contact>();
        while (reader.Read()) contacts.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return new(contacts.ToArray(), page, page * 6 < total ? page + 1 : null, total);
    }

    public void Add(string name, string email, string team)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO contacts (name, email, team) VALUES ($name, $email, $team)";
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$email", email.Trim());
        command.Parameters.AddWithValue("$team", team);
        command.ExecuteNonQuery();
    }

    public bool Update(int id, string name, string email, string team) => Mutate("UPDATE contacts SET name = $name, email = $email, team = $team WHERE id = $id", id, name, email, team);
    public bool Delete(int id) => Mutate("DELETE FROM contacts WHERE id = $id", id, "", "", "");

    private bool Mutate(string sql, int id, string name, string email, string team)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$email", email.Trim());
        command.Parameters.AddWithValue("$team", team);
        return command.ExecuteNonQuery() > 0;
    }
}
