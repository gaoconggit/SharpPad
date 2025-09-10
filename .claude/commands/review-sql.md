# /review-sql - SQL Review Command

Launches the professional SQL review agent for comprehensive database script analysis covering safety, performance, and security.

## Syntax

```
/review-sql [file_path | sql_statement | pattern] [options]
```

## Description

Activates the `sql-reviewer` agent to perform thorough SQL analysis covering:
- Data safety and risk assessment
- Performance optimization opportunities
- Security vulnerability detection
- Database-specific best practices
- Execution timing and resource planning

## Examples

### Basic Usage

#### Review SQL file
```
/review-sql migrations/001_create_users_table.sql
```

#### Review SQL statement
```
/review-sql
SELECT * FROM users 
WHERE created_at > '2024-01-01' 
ORDER BY id DESC
```

#### Review current file
```
/review-sql
```

#### Review multiple files
```
/review-sql migrations/*.sql
```

### Advanced Usage

#### Production deployment review
```
/review-sql --env=prod --strict schema_changes.sql
```

#### Performance-focused review
```
/review-sql --focus=performance slow_queries.sql
```

#### Security audit
```
/review-sql --focus=security user_operations.sql
```

#### Database-specific review
```
/review-sql --db=sqlserver --env=prod maintenance.sql
```

## Parameters

### Database Engine
- `--db=sqlserver` - SQL Server specific optimizations and checks
- `--db=mysql` - MySQL specific recommendations  
- `--db=postgresql` - PostgreSQL best practices
- `--db=oracle` - Oracle performance and syntax guidance

### Environment Context
- `--env=prod` - Production deployment safety checks
- `--env=staging` - Staging environment validation
- `--env=dev` - Development environment review

### Focus Areas
- `--focus=security` - Emphasize security vulnerability detection
- `--focus=performance` - Prioritize query optimization analysis
- `--focus=safety` - Focus on data safety and backup requirements

### Review Depth
- `--strict` - Strictest review mode with all safety checks
- `--quick` - Fast review focusing on critical issues only
- `--estimate` - Include execution time and resource estimates

## Review Categories

The command produces structured output with issues categorized by risk level:

### 🔴 Critical Issues (Must Fix Before Execution)
- **Data Safety**: DELETE/UPDATE without WHERE clauses
- **Security**: SQL injection vulnerabilities, unsafe dynamic SQL
- **Breaking Changes**: Schema modifications affecting existing data
- **Resource Risks**: Operations that could cause system outages

### ⚠️ Performance Issues (Should Address)
- **Query Optimization**: Missing indexes, inefficient joins
- **Resource Usage**: Large result sets, expensive operations
- **Database Load**: Operations during business hours
- **Scalability**: Queries that don't scale with data growth

### 💡 Best Practice Suggestions (Consider)
- **Naming Conventions**: Table and column naming standards
- **Code Quality**: SQL formatting and maintainability
- **Documentation**: Missing comments for complex logic
- **Standards Compliance**: Framework and team conventions

## Business Hours Safety

### Execution Time Guidelines
- **Business Hours (09:00-18:00)**: Avoid large data operations
- **Recommended Windows**: 
  - Early morning (06:00-09:00)
  - Evening (18:00-22:00)
  - Weekends for major operations
- **Emergency Operations**: Require special approval and notification

### Data Safety Requirements
The command automatically evaluates:
- Backup requirements before destructive operations
- Estimated execution time and resource impact
- Rollback plan necessity
- Change window recommendations

## Risk Assessment

### 🔴 High Risk Operations
- DELETE/UPDATE without WHERE clauses
- DROP TABLE/DATABASE statements
- ALTER TABLE data type changes
- Full table scans on large tables
- Cross-database operations

### 🟡 Medium Risk Operations
- Complex multi-table joins
- Subqueries with potential performance impact
- Operations affecting indexed columns
- Bulk INSERT operations

### 🟢 Low Risk Operations
- Simple SELECT queries with proper WHERE clauses
- Standard INSERT with validation
- Index creation statements
- View creation or modification

## Output Format

The command provides structured feedback with risk-categorized analysis:

```
### Review Summary
- **Review Status**: ❌ Changes Required
- **Database Engine**: MySQL 8.0
- **Statement Types**: DELETE, UPDATE, SELECT
- **Tables Affected**: users, user_profiles, user_logs
- **Risk Level**: 🔴 High

### 🔴 Critical Issues (Must Fix)
- **Line 5**: DELETE FROM users - Missing WHERE clause will delete all records
- **Line 12**: UPDATE during business hours - Operation scheduled during peak time

### ⚠️ Performance Issues (Should Fix)
- **Line 18**: SELECT * FROM large_table - Specify required columns only
- **Line 25**: Subquery could be optimized with JOIN

### 💡 Style Suggestions (Consider)
- **Line 8**: Table alias could improve readability
- **Line 15**: Add comment explaining business logic

### Execution Recommendations
- **Timing**: Execute during maintenance window (22:00-06:00)
- **Backup**: Full backup required before DELETE operations
- **Rollback Plan**: Prepare restoration scripts for affected tables
- **Monitoring**: Add progress logging for batch operations
```

## Best Practices

### Deployment Pipeline Integration

#### Development Workflow
```bash
# Review during development
/review-sql --env=dev new_feature.sql

# Pre-staging validation
/review-sql --env=staging --focus=performance migrations/*.sql

# Production deployment review
/review-sql --env=prod --strict release_scripts.sql
```

#### Automated Checks
```bash
# Git pre-commit hook for SQL files
git diff --cached --name-only | grep '\.sql$' | xargs /review-sql --quick

# CI/CD pipeline integration
find sql/ -name "*.sql" -newer last_build | xargs /review-sql --env=prod
```

### Migration Safety Protocol
1. **Development**: `/review-sql --env=dev` for initial validation
2. **Testing**: `/review-sql --env=staging` for performance verification
3. **Production**: `/review-sql --env=prod --strict` for final approval
4. **Execution**: Follow timing and backup recommendations

## Integration

This command automatically invokes the `sql-reviewer` agent with database-specific optimizations based on the provided parameters. The agent applies the same safety, performance, and security criteria as defined in its configuration.

## Related Commands

- `/review-code` - General code quality review
- `@sql-reviewer` - Direct agent invocation with custom instructions