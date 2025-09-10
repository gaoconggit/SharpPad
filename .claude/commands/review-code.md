# /review-code - Code Review Command

Launches the professional code review agent for comprehensive quality, security, and maintainability analysis.

## Syntax

```
/review-code [file_path | code_snippet | pattern]
```

## Description

Activates the `code-reviewer` agent to perform thorough code analysis covering:
- Logic correctness and edge cases
- Security vulnerabilities and best practices  
- Performance optimization opportunities
- Code maintainability and standards compliance
- Framework-specific recommendations

## Examples

### Basic Usage

#### Review current file
```
/review-code
```

#### Review specific file
```
/review-code src/utils/helper.js
```

#### Review code snippet
```
/review-code
function validateUser(user) {
    if (user.name && user.email) {
        return true;
    }
    return false;
}
```

#### Review multiple files
```
/review-code src/components/*.tsx
```

### Advanced Usage

#### Focus on security issues
```
/review-code --focus=security UserController.cs
```

#### Performance optimization review
```
/review-code --focus=performance src/data/queries.js
```

#### Review recent changes
```
/review-code --changes
```

## Parameters

### Focus Areas
- `--focus=security` - Prioritize security vulnerability analysis
- `--focus=performance` - Emphasize performance optimization
- `--focus=maintainability` - Focus on code structure and readability
- `--focus=standards` - Check coding standards and conventions

### Scope Options
- `--changes` - Review only recent Git changes
- `--modified` - Review modified files in working directory
- `--staged` - Review staged files for commit

### Review Depth
- `--quick` - Fast review focusing on critical issues only
- `--thorough` - Comprehensive analysis including style suggestions
- `--strict` - Strictest review mode with all checks enabled

## Review Categories

The command produces structured output with issues categorized by severity:

### 🔴 Critical Issues (Must Fix)
- Logic errors and potential runtime failures
- Security vulnerabilities (SQL injection, XSS, authentication bypasses)
- Performance problems (memory leaks, blocking operations)
- Breaking changes or API compatibility issues

### ⚠️ Quality Improvements (Should Fix)
- Code structure issues (duplication, complex methods)
- Missing error handling or validation
- Resource management problems
- Insufficient test coverage

### 💡 Style Suggestions (Consider)
- Naming convention improvements
- Code formatting and organization
- Documentation gaps
- Framework-specific best practices

## Best Practices

### When to Use
- **Pre-commit**: Quality check before committing changes
- **Pull Request Review**: Comprehensive analysis for merge requests
- **Refactoring Safety**: Ensure changes don't introduce issues
- **Learning**: Understand coding standards and best practices
- **Security Audits**: Focus on vulnerability detection

### Integration Examples

#### Git Workflow Integration
```bash
# Review staged changes before commit
/review-code --staged

# Review changes in current branch
/review-code --changes

# Review specific commit
git show --name-only HEAD | xargs /review-code
```

#### Pre-commit Hook
```bash
#!/bin/sh
# .git/hooks/pre-commit
changed_files=$(git diff --cached --name-only --diff-filter=ACM | grep -E '\.(js|ts|cs|py)$')
if [ -n "$changed_files" ]; then
    /review-code $changed_files --quick
fi
```

### Output Format

The command provides structured feedback with specific file references:

```
### Review Summary
- **Files Reviewed**: UserService.cs, UserController.cs
- **Change Type**: Feature enhancement
- **Overall Assessment**: ⚠️ Approved with Comments

### Critical Issues (❌ Must Fix)
- `UserService.cs:45` - SQL injection vulnerability in user lookup
- `UserController.cs:23` - Missing authentication check

### Quality Improvements (⚠️ Should Fix)
- `UserService.cs:12` - Missing error handling for database operations
- `UserController.cs:67` - Method complexity too high (15 lines)

### Style Suggestions (💡 Consider)
- `UserService.cs:8` - Consider more descriptive variable names
- `UserController.cs:34` - Add XML documentation for public methods
```

## Integration

This command automatically invokes the `code-reviewer` agent with optimized parameters based on the provided options. The agent uses the same review criteria as defined in its configuration, ensuring consistent quality standards across your codebase.

## Related Commands

- `/review-sql` - Specialized SQL script review
- `@code-reviewer` - Direct agent invocation with custom instructions