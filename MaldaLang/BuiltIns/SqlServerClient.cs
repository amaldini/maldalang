// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// SQL Server database client for MALDA with connection management, raw SQL queries, parameterized queries, and query builder support.
/// </summary>
public class SqlServerClientInstance : ObjectInstance, IDisposable
{
    private string? _connectionString;
    private SqlConnection? _connection;
    private SqlTransaction? _transaction;
    private QueryBuilder? _queryBuilder;
    private bool _disposed = false;
    
    public bool IsConnected => !_disposed && _connection?.State == ConnectionState.Open;
    
    public SqlServerClientInstance() : base(null)
    {
    }
    
    public SqlServerClientInstance(string? connectionString) : base(null)
    {
        _connectionString = connectionString;
        if (!string.IsNullOrEmpty(connectionString))
        {
            _connection = new SqlConnection(connectionString);
        }
    }
    
    /// <summary>
    /// Finalizer - ensures connection is cleaned up even if Dispose() is not called.
    /// </summary>
    ~SqlServerClientInstance()
    {
        Dispose(false);
    }
    
    /// <summary>
    /// Disposes the database client and closes any open connections.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    /// <summary>
    /// Protected implementation of Dispose pattern.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources
                Disconnect();
            }
            // Clean up unmanaged resources (if any)
            _disposed = true;
        }
    }
    
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SqlServerClientInstance), "SqlServerClient has been disposed.");
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle property access
        if (name == "isConnected")
            return RuntimeValue.Boolean(IsConnected);
        
        // Handle method access
        if (name == "connect" || name == "disconnect" || name == "query" || name == "queryOne" ||
            name == "execute" || name == "beginTransaction" || name == "commit" || name == "rollback" ||
            name == "select" || name == "from" || name == "where" || name == "insert" || 
            name == "update" || name == "delete")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on SqlServerClient.");
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        ThrowIfDisposed();
        
        switch (methodName)
        {
            case "connect":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("connect() expects 1 string argument (connectionString)");
                Connect(args[0].AsString());
                return RuntimeValue.Null();
            
            case "disconnect":
                if (args.Count != 0)
                    throw new Exception("disconnect() expects 0 arguments");
                Disconnect();
                return RuntimeValue.Null();
            
            case "query":
                if (args.Count == 0)
                    return Query();
                if (args[0].Type != ValueType.String)
                    throw new Exception("query() expects at least 1 string argument (sql)");
                var sql = args[0].AsString();
                var queryParams = args.Count > 1 ? args[1] : null;
                return Query(sql, queryParams);
            
            case "queryOne":
                if (args.Count < 1 || args[0].Type != ValueType.String)
                    throw new Exception("queryOne() expects at least 1 string argument (sql)");
                var sqlOne = args[0].AsString();
                var queryParamsOne = args.Count > 1 ? args[1] : null;
                return QueryOne(sqlOne, queryParamsOne);
            
            case "execute":
                if (args.Count < 1 || args[0].Type != ValueType.String)
                    throw new Exception("execute() expects at least 1 string argument (sql)");
                var executeSql = args[0].AsString();
                var executeParams = args.Count > 1 ? args[1] : null;
                return Execute(executeSql, executeParams);
            
            case "beginTransaction":
                if (args.Count != 0)
                    throw new Exception("beginTransaction() expects 0 arguments");
                BeginTransaction();
                return RuntimeValue.Null();
            
            case "commit":
                if (args.Count != 0)
                    throw new Exception("commit() expects 0 arguments");
                Commit();
                return RuntimeValue.Null();
            
            case "rollback":
                if (args.Count != 0)
                    throw new Exception("rollback() expects 0 arguments");
                Rollback();
                return RuntimeValue.Null();
            
            case "select":
                if (args.Count == 0)
                    throw new Exception("select() expects at least 1 argument (column names)");
                var columns = args.Select(a => a.AsString()).ToArray();
                return Select(columns);
            
            case "from":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("from() expects 1 string argument (table name)");
                return From(args[0].AsString());
            
            case "where":
                if (args.Count < 2 || args.Count > 3)
                    throw new Exception("where() expects 2 or 3 arguments: (column, operator, value?) or (column, value)");
                if (args.Count == 2)
                {
                    return Where(args[0].AsString(), "=", args[1]);
                }
                else
                {
                    if (args[1].Type != ValueType.String)
                        throw new Exception("where() operator must be a string");
                    return Where(args[0].AsString(), args[1].AsString(), args[2]);
                }
            
            case "insert":
                if (args.Count < 2 || args[0].Type != ValueType.String)
                    throw new Exception("insert() expects at least 2 arguments: (table, values)");
                var table = args[0].AsString();
                var values = args[1];
                return Insert(table, values);
            
            case "update":
                if (args.Count < 2 || args[0].Type != ValueType.String)
                    throw new Exception("update() expects at least 2 arguments: (table, values)");
                var updateTable = args[0].AsString();
                var updateValues = args[1];
                return Update(updateTable, updateValues);
            
            case "delete":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("delete() expects 1 string argument (table name)");
                return Delete(args[0].AsString());
            
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }
    
    private void Connect(string connectionString)
    {
        ThrowIfDisposed();
        _connectionString = connectionString;
        if (_connection != null)
        {
            _connection.Dispose();
        }
        _connection = new SqlConnection(connectionString);
        _connection.Open();
    }
    
    private void Disconnect()
    {
        if (_disposed)
            return;
            
        if (_transaction != null)
        {
            try
            {
                _transaction.Rollback();
            }
            catch { } // Ignore errors during rollback in cleanup
            try
            {
                _transaction.Dispose();
            }
            catch { } // Ignore errors during disposal
            _transaction = null;
        }
        
        if (_connection != null)
        {
            try
            {
                if (_connection.State == ConnectionState.Open)
                {
                    _connection.Close();
                }
            }
            catch { } // Ignore errors during close
            try
            {
                _connection.Dispose();
            }
            catch { } // Ignore errors during disposal
            _connection = null;
        }
    }
    
    private RuntimeValue Query(string sql, RuntimeValue? parameters)
    {
        ThrowIfDisposed();
        EnsureConnected();
        
        try
        {
            using var command = new SqlCommand(sql, _connection, _transaction);
            AddParameters(command, parameters);
            
            using var reader = command.ExecuteReader();
            var results = new List<RuntimeValue>();
            
            while (reader.Read())
            {
                var row = new JsonObject();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var columnName = reader.GetName(i);
                    var value = reader.GetValue(i);
                    row.Set(columnName, ConvertToRuntimeValue(value));
                }
                results.Add(RuntimeValue.Object(row));
            }
            
            return RuntimeValue.Array(results);
        }
        catch (Exception ex)
        {
            throw new RuntimeException($"SQL query error: {ex.Message}");
        }
    }
    
    private RuntimeValue QueryOne(string sql, RuntimeValue? parameters)
    {
        ThrowIfDisposed();
        EnsureConnected();
        
        try
        {
            using var command = new SqlCommand(sql, _connection, _transaction);
            AddParameters(command, parameters);
            
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return RuntimeValue.Null();
            }
            
            var row = new JsonObject();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var columnName = reader.GetName(i);
                var value = reader.GetValue(i);
                row.Set(columnName, ConvertToRuntimeValue(value));
            }
            
            return RuntimeValue.Object(row);
        }
        catch (Exception ex)
        {
            throw new RuntimeException($"SQL query error: {ex.Message}");
        }
    }
    
    private RuntimeValue Execute(string sql, RuntimeValue? parameters)
    {
        ThrowIfDisposed();
        EnsureConnected();
        
        try
        {
            using var command = new SqlCommand(sql, _connection, _transaction);
            AddParameters(command, parameters);
            
            var rowsAffected = command.ExecuteNonQuery();
            return RuntimeValue.Integer(rowsAffected);
        }
        catch (Exception ex)
        {
            throw new RuntimeException($"SQL execute error: {ex.Message}");
        }
    }
    
    private void BeginTransaction()
    {
        ThrowIfDisposed();
        EnsureConnected();
        if (_transaction != null)
            throw new RuntimeException("Transaction already in progress");
        _transaction = _connection!.BeginTransaction();
    }
    
    private void Commit()
    {
        ThrowIfDisposed();
        if (_transaction == null)
            throw new RuntimeException("No transaction in progress");
        _transaction.Commit();
        _transaction.Dispose();
        _transaction = null;
    }
    
    private void Rollback()
    {
        ThrowIfDisposed();
        if (_transaction == null)
            throw new RuntimeException("No transaction in progress");
        _transaction.Rollback();
        _transaction.Dispose();
        _transaction = null;
    }
    
    private RuntimeValue Select(params string[] columns)
    {
        ThrowIfDisposed();
        _queryBuilder = new QueryBuilder { Type = QueryType.Select, Columns = columns.ToList() };
        return RuntimeValue.Object(this);
    }
    
    private RuntimeValue From(string table)
    {
        ThrowIfDisposed();
        if (_queryBuilder == null || _queryBuilder.Type != QueryType.Select)
            throw new RuntimeException("from() must be called after select()");
        _queryBuilder.Table = table;
        return RuntimeValue.Object(this);
    }
    
    private RuntimeValue Where(string column, string op, RuntimeValue value)
    {
        ThrowIfDisposed();
        if (_queryBuilder == null)
            throw new RuntimeException("where() must be called after select() and from()");
        _queryBuilder.WhereConditions.Add(new WhereCondition { Column = column, Operator = op, Value = value });
        return RuntimeValue.Object(this);
    }
    
    private RuntimeValue Query()
    {
        ThrowIfDisposed();
        if (_queryBuilder == null)
            throw new RuntimeException("Query builder not initialized. Call select() first.");
        
        var sql = _queryBuilder.BuildSql();
        
        // Build parameters from where conditions
        var allParams = new JsonObject();
        foreach (var condition in _queryBuilder.WhereConditions)
        {
            var paramName = "@" + condition.Column;
            allParams.Set(paramName, condition.Value);
        }
        
        var paramsRuntimeValue = allParams.GetProperties().Count > 0 ? RuntimeValue.Object(allParams) : null;
        var result = Query(sql, paramsRuntimeValue);
        _queryBuilder = null;
        return result;
    }
    
    private RuntimeValue Insert(string table, RuntimeValue values)
    {
        ThrowIfDisposed();
        if (values.Type != ValueType.Object)
            throw new RuntimeException("insert() values must be an object");
        
        var valuesObj = values.AsObject();
        var properties = GetObjectProperties(valuesObj);
        
        if (properties.Count == 0)
            throw new RuntimeException("insert() values object cannot be empty");
        
        var columns = string.Join(", ", properties.Keys);
        var paramNames = string.Join(", ", properties.Keys.Select(k => "@" + k));
        var sql = $"INSERT INTO {table} ({columns}) VALUES ({paramNames})";
        
        var paramsObj = new JsonObject();
        foreach (var kvp in properties)
        {
            paramsObj.Set(kvp.Key, kvp.Value);
        }
        var paramsRuntimeValue = RuntimeValue.Object(paramsObj);
        
        var result = Execute(sql, paramsRuntimeValue);
        _queryBuilder = null;
        return result;
    }
    
    private RuntimeValue Update(string table, RuntimeValue values)
    {
        ThrowIfDisposed();
        if (values.Type != ValueType.Object)
            throw new RuntimeException("update() values must be an object");
        
        var valuesObj = values.AsObject();
        var properties = GetObjectProperties(valuesObj);
        
        if (properties.Count == 0)
            throw new RuntimeException("update() values object cannot be empty");
        
        if (_queryBuilder == null || _queryBuilder.WhereConditions.Count == 0)
            throw new RuntimeException("update() requires where() conditions");
        
        // Combine values and where parameters
        // For UPDATE, we need to use different parameter names for SET and WHERE clauses
        var allParams = new JsonObject();
        foreach (var kvp in properties)
        {
            var paramName = "@set_" + kvp.Key; // Prefix to avoid conflicts
            allParams.Set(paramName, kvp.Value);
        }
        foreach (var condition in _queryBuilder.WhereConditions)
        {
            var paramName = "@where_" + condition.Column; // Prefix to avoid conflicts
            allParams.Set(paramName, condition.Value);
        }
        
        // Update SQL to use prefixed parameter names
        var setClause = string.Join(", ", properties.Keys.Select(k => $"{k} = @set_{k}"));
        var sql = $"UPDATE {table} SET {setClause}";
        var whereSql = _queryBuilder.BuildWhereClauseWithPrefix("@where_");
        sql += " WHERE " + whereSql;
        
        var paramsRuntimeValue = RuntimeValue.Object(allParams);
        var result = Execute(sql, paramsRuntimeValue);
        _queryBuilder = null;
        return result;
    }
    
    private RuntimeValue Delete(string table)
    {
        ThrowIfDisposed();
        if (_queryBuilder == null || _queryBuilder.WhereConditions.Count == 0)
            throw new RuntimeException("delete() requires where() conditions");
        
        // Build parameters from where conditions with prefix
        var allParams = new JsonObject();
        foreach (var condition in _queryBuilder.WhereConditions)
        {
            var paramName = "@where_" + condition.Column;
            allParams.Set(paramName, condition.Value);
        }
        
        // Update SQL to use prefixed parameter names
        var whereSql = _queryBuilder.BuildWhereClauseWithPrefix("@where_");
        var sql = $"DELETE FROM {table} WHERE " + whereSql;
        
        var paramsRuntimeValue = RuntimeValue.Object(allParams);
        var result = Execute(sql, paramsRuntimeValue);
        _queryBuilder = null;
        return result;
    }
    
    private void EnsureConnected()
    {
        if (_connection == null || !IsConnected)
            throw new RuntimeException("Not connected to database. Call connect() first.");
    }
    
    private void AddParameters(SqlCommand command, RuntimeValue? parameters)
    {
        if (parameters == null || parameters.Type != ValueType.Object)
            return;
        
        var paramsObj = parameters.AsObject();
        
        // Handle JsonObject
        if (paramsObj is JsonObject jsonObj)
        {
            var props = jsonObj.GetProperties();
            foreach (var kvp in props)
            {
                var paramName = kvp.Key.StartsWith("@") ? kvp.Key : "@" + kvp.Key;
                var param = new SqlParameter(paramName, ConvertToDbValue(kvp.Value));
                command.Parameters.Add(param);
            }
            return;
        }
        
        // For other objects, try to get properties
        var properties = GetObjectProperties(paramsObj);
        foreach (var kvp in properties)
        {
            var paramName = kvp.Key.StartsWith("@") ? kvp.Key : "@" + kvp.Key;
            var param = new SqlParameter(paramName, ConvertToDbValue(kvp.Value));
            command.Parameters.Add(param);
        }
    }
    
    private Dictionary<string, RuntimeValue> GetObjectProperties(ObjectInstance obj)
    {
        var properties = new Dictionary<string, RuntimeValue>();
        
        // Handle JsonObject specially
        if (obj is JsonObject jsonObj)
        {
            return jsonObj.GetProperties();
        }
        
        // For other ObjectInstance objects, we need to access their fields
        // Since _fields is private, we'll use reflection as a fallback
        try
        {
            var fields = obj.GetType().GetField("_fields", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (fields != null)
            {
                var value = fields.GetValue(obj);
                if (value is Dictionary<string, RuntimeValue> props)
                {
                    return props;
                }
            }
        }
        catch { }
        
        return properties;
    }
    
    private RuntimeValue ConvertToRuntimeValue(object? value)
    {
        if (value == null || value == DBNull.Value)
            return RuntimeValue.Null();
        
        return value switch
        {
            int i => RuntimeValue.Integer(i),
            long l => RuntimeValue.Integer((int)l),
            short s => RuntimeValue.Integer(s),
            byte b => RuntimeValue.Integer(b),
            double d => RuntimeValue.Float(d),
            float f => RuntimeValue.Float(f),
            decimal dec => RuntimeValue.Float((double)dec),
            string s => RuntimeValue.String(s),
            bool b => RuntimeValue.Boolean(b),
            DateTime dt => RuntimeValue.String(dt.ToString("yyyy-MM-dd HH:mm:ss")),
            Guid g => RuntimeValue.String(g.ToString()),
            _ => RuntimeValue.String(value.ToString() ?? "")
        };
    }
    
    private object? ConvertToDbValue(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.Integer => value.AsInteger(),
            ValueType.Float => value.AsFloat(),
            ValueType.String => value.AsString(),
            ValueType.Boolean => value.AsBoolean(),
            ValueType.Null => DBNull.Value,
            _ => value.ToString()
        };
    }
    
    private class QueryBuilder
    {
        public QueryType Type { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? Table { get; set; }
        public List<WhereCondition> WhereConditions { get; set; } = new();
        
        public string BuildSql()
        {
            if (Type == QueryType.Select)
            {
                var columns = Columns.Count > 0 ? string.Join(", ", Columns) : "*";
                var sql = $"SELECT {columns} FROM {Table}";
                if (WhereConditions.Count > 0)
                {
                    sql += " WHERE " + BuildWhereClause();
                }
                return sql;
            }
            throw new RuntimeException("Unsupported query type");
        }
        
        public string BuildWhereClause()
        {
            var conditions = new List<string>();
            for (int i = 0; i < WhereConditions.Count; i++)
            {
                var condition = WhereConditions[i];
                conditions.Add($"{condition.Column} {condition.Operator} @{condition.Column}");
            }
            return string.Join(" AND ", conditions);
        }
        
        public string BuildWhereClauseWithPrefix(string prefix)
        {
            var conditions = new List<string>();
            for (int i = 0; i < WhereConditions.Count; i++)
            {
                var condition = WhereConditions[i];
                conditions.Add($"{condition.Column} {condition.Operator} {prefix}{condition.Column}");
            }
            return string.Join(" AND ", conditions);
        }
    }
    
    private enum QueryType
    {
        Select,
        Insert,
        Update,
        Delete
    }
    
    private class WhereCondition
    {
        public string Column { get; set; } = "";
        public string Operator { get; set; } = "=";
        public RuntimeValue Value { get; set; }
    }
}
