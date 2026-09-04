// ANTLR4 grammar for the Jet/ACE (Microsoft Access) SQL dialect.
//
// Scope: SELECT with projection/aliases, multi-table FROM with INNER/LEFT/RIGHT/FULL JOIN and
// derived-table subqueries, WHERE, ORDER BY, TOP. The parse tree is lowered into
// LibRed.Sql.Ast by AstBuilder, so the rest of the engine never sees these generated types.
//
// Dialect notes (vs ANSI): '&' string concat; MOD / '\' operators; '*'/'?' LIKE wildcards;
// TOP n (no OFFSET); #1/1/2020# date literals; [bracketed] and `backtick` identifiers;
// booleans -1/0.

grammar AccessSql;

// A single statement, optionally terminated by ';' (EF Core emits a trailing semicolon).
statement : parametersClause? (ifThenStatement | createTableStatement | createIndexStatement | createViewStatement | createProcedureStatement | alterTableStatement | dropStatement | insertStatement | updateStatement | deleteStatement | transactionStatement | executeStatement | systemVariableSelect | queryExpression) SEMI? EOF ;

// EF emits Jet's conditional DDL for idempotent migrations: `IF [NOT] EXISTS (<select>) THEN <statement>`.
// A single guarded statement (all EF produces); the condition is an ordinary subquery (e.g. over INFORMATION_SCHEMA).
ifThenStatement : IF not=NOT? EXISTS LPAREN selectStatement RPAREN THEN thenBody ;
thenBody : createTableStatement | createIndexStatement | createViewStatement | createProcedureStatement | alterTableStatement | dropStatement | insertStatement | updateStatement | deleteStatement | executeStatement | queryExpression ;

// EXECUTE|EXEC procedure [arg [, arg …]] — invoke a stored procedure/query by name with positional
// argument values (Access syntax has no parentheses). The args bind to the procedure's declared parameters.
executeStatement : (EXECUTE | EXEC) name=identifier (expression (COMMA expression)*)? ;

// UPDATE table SET col = expr, … [WHERE criteria]. The WHERE criteria is an ordinary expression, the same
// as a SELECT's; each SET value expression may reference the row's current column values.
// UPDATE tableexpression SET col=expr, … [WHERE …]. The tableexpression is a table SOURCE (Access allows a
// join here), and a SET target may be table-qualified (col or alias.col) to touch a specific joined table.
updateStatement : UPDATE tableSource SET assignment (COMMA assignment)* whereClause? ;
assignment : target=columnRef EQ expression ;

// DELETE [table.* | *] FROM tableexpression [WHERE …]. For a join, the `table.*` target selects which
// table's rows to delete; a bare `*` (or no target) is only valid for a single table — a join without a
// `table.*` target is ambiguous and rejected at execution (matching Access, which asks you to specify it).
deleteStatement : DELETE (target=identifier DOT STAR | STAR)? FROM tableSource whereClause? ;

// A FROM-less SELECT of system variables only — ACE allows `SELECT @@IDENTITY` / `SELECT @@ROWCOUNT`
// (and a comma list of them) with no FROM clause. Listed before queryExpression so it is preferred; a
// regular SELECT still requires a FROM (its selectList can also contain @@vars, e.g. `SELECT @@IDENTITY
// FROM t`). Since the projection must be all SYSVAR tokens, this can't shadow an ordinary SELECT.
systemVariableSelect : SELECT sysVarItem (COMMA sysVarItem)* ;
sysVarItem : SYSVAR (AS? alias=identifier)? ;

// A leading PARAMETERS clause (Access) declares the query's parameters up front. Used when reading a
// stored parameterized query back: references to a declared name in the body bind as parameters, not
// columns.  PARAMETERS p1 datatype, p2 datatype ;
parametersClause : PARAMETERS procParam (COMMA procParam)* SEMI ;

// ---- DDL / DML ----

createTableStatement
    : CREATE temp=TEMPORARY? TABLE table=identifier
      LPAREN columnDefinition (COMMA columnDefinition)* (COMMA tableConstraint)* RPAREN
    ;

// CREATE VIEW view [(field1, …)] AS selectstatement. The body is a SELECT (UNION accepted too).
createViewStatement
    : CREATE VIEW name=identifier
      (LPAREN columns+=identifier (COMMA columns+=identifier)* RPAREN)?
      AS query=queryExpression
    ;

// CREATE PROCEDURE name [param datatype, …] AS <select> — a parameterized stored query (no parens
// around the params, per the Access DDL). Stored like a view plus MSysQueries parameter rows.
createProcedureStatement
    : CREATE PROCEDURE name=identifier
      procParamList?
      AS body=procedureBody
    ;
// The parameter list is optional, and Access accepts it either bare or wrapped in parentheses.
procParamList
    : LPAREN procParam (COMMA procParam)* RPAREN
    | procParam (COMMA procParam)*
    ;
// A parameter name is an identifier or an @-prefixed parameter token (e.g. @Beginning_Date).
procParam : pname=procParamName dataType ;
procParamName : identifier | PARAM ;

// ALTER TABLE table { ADD [COLUMN] field type … | ADD CONSTRAINT … | ALTER COLUMN field type | DROP … }
// (Access allows exactly one action per statement.) The CONSTRAINT clause reuses CREATE TABLE's
// tableConstraint (PK / FK / UNIQUE / CHECK — the "multifieldindex"). COLUMN is optional on ADD (EF omits it).
alterTableStatement
    : ALTER TABLE table=identifier alterTableAction
    ;
alterTableAction
    : ADD COLUMN? columnDefinition               # AddColumnAction
    | ADD tableConstraint                        # AddConstraintAction
    | ALTER COLUMN? field=identifier dataType columnConstraint*  # AlterColumnAction
    | ALTER COLUMN? field=identifier SET DEFAULT expression       # AlterColumnSetDefaultAction
    | ALTER COLUMN? field=identifier DROP DEFAULT                 # AlterColumnDropDefaultAction
    | DROP COLUMN field=identifier               # DropColumnAction
    | DROP CONSTRAINT cname=identifier           # DropConstraintAction
    // Renames. Jet/ACE has no rename syntax at all (Access does them through DAO/ADOX), so these are the
    // provider's own DDL — kept as one ALTER TABLE family so the table is always the statement's subject.
    | RENAME TO newName=identifier                                  # RenameTableAction
    | RENAME COLUMN field=identifier TO newName=identifier          # RenameColumnAction
    | RENAME INDEX index=identifier TO newName=identifier           # RenameIndexAction
    ;

// A procedure body is any statement Access allows. We store/execute the ones we know (SELECT, INSERT,
// CREATE TABLE); other action queries (UPDATE/DELETE/DROP/…) are rejected by the builder.
procedureBody : queryExpression | insertStatement | createTableStatement ;

// CREATE [UNIQUE] INDEX name ON table (field [ASC|DESC], …) [WITH {PRIMARY|DISALLOW NULL|IGNORE NULL}]
createIndexStatement
    : CREATE unique=UNIQUE? INDEX name=identifier ON table=identifier
      LPAREN indexColumn (COMMA indexColumn)* RPAREN
      (WITH withOption)?
    ;

// DROP { TABLE table | INDEX index ON table | PROCEDURE procedure | VIEW view } — deletes an object
// (Access "DROP statement"). One target per statement; DROP INDEX names the table the index is on.
dropStatement
    : DROP TABLE table=identifier                       # DropTableStatement
    | DROP INDEX index=identifier ON table=identifier   # DropIndexStatement
    | DROP PROCEDURE proc=identifier                    # DropProcedureStatement
    | DROP VIEW view=identifier                         # DropViewStatement
    ;

indexColumn : col=identifier dir=(ASC | DESC)? ;

withOption
    : PRIMARY        # WithPrimary
    | DISALLOW NULL  # WithDisallowNull
    | IGNORE NULL    # WithIgnoreNull
    ;

columnDefinition : name=identifier dataType columnConstraint* ;

// A second word handles two-word ANSI aliases like CHARACTER VARYING / BIT VARYING.
// Up to three words to cover multi-word SQL type names: "char varying", "national character varying", etc.
dataType : typeName=identifier extra=identifier? extra2=identifier? (LPAREN size=signedInteger (COMMA scale=signedInteger)? RPAREN)? ;

// A possibly-negative integer — needed for a descending COUNTER(seed, increment) whose increment is negative.
signedInteger : MINUS? INTEGER_LITERAL ;

// Single-field constraints (after the column's data type). A CONSTRAINT name may prefix any of them.
columnConstraint
    : NOT NULL                                       # NotNullConstraint
    | NULL                                           # NullableConstraint
    | DEFAULT expression                             # DefaultConstraint
    | WITH (COMPRESSION | COMP)                       # CompressionConstraint
    | (CONSTRAINT cname=identifier)? CHECK LPAREN checkBody RPAREN  # CheckColumnConstraint
    | (CONSTRAINT cname=identifier)? PRIMARY KEY     # PrimaryKeyConstraint
    | (CONSTRAINT cname=identifier)? UNIQUE          # UniqueColumnConstraint
    | (CONSTRAINT cname=identifier)? REFERENCES refTable=identifier
        (LPAREN refColumns+=identifier (COMMA refColumns+=identifier)* RPAREN)?
        foreignKeyAction*                            # ColumnReferencesConstraint
    ;

// EF Core emits named table constraints: CONSTRAINT `PK_x` PRIMARY KEY (`col`, ...) and
// CONSTRAINT `FK_x` FOREIGN KEY (`col`, ...) REFERENCES `Parent` (`col`, ...) ON DELETE CASCADE.
tableConstraint
    : (CONSTRAINT name=identifier)? PRIMARY KEY
        LPAREN columns+=identifier (COMMA columns+=identifier)* RPAREN                        # PrimaryKeyTableConstraint
    | (CONSTRAINT name=identifier)? UNIQUE
        LPAREN columns+=identifier (COMMA columns+=identifier)* RPAREN                        # UniqueTableConstraint
    | (CONSTRAINT name=identifier)? FOREIGN KEY (noIndex=NO INDEX)?
        LPAREN columns+=identifier (COMMA columns+=identifier)* RPAREN
        REFERENCES refTable=identifier
        (LPAREN refColumns+=identifier (COMMA refColumns+=identifier)* RPAREN)?
        foreignKeyAction*                                                                      # ForeignKeyTableConstraint
    | (CONSTRAINT name=identifier)? CHECK LPAREN checkBody RPAREN                               # CheckTableConstraint
    ;

// A CHECK expression is parsed as balanced-paren token soup and ignored (not enforced yet), so any
// expression ACE accepts parses without needing full expression support.
checkBody : ( ~(LPAREN | RPAREN) | LPAREN checkBody RPAREN )* ;

// ON UPDATE / ON DELETE may appear in either order (Access documents UPDATE-then-DELETE; EF Core
// emits only ON DELETE), so they are parsed as an unordered list.
foreignKeyAction
    : ON UPDATE referentialAction   # OnUpdateAction
    | ON DELETE referentialAction   # OnDeleteAction
    ;

// Referential actions (ON DELETE / ON UPDATE). Jet's DAO model records only enforce / cascade
// update / cascade delete; NO ACTION / RESTRICT map to "enforced, no cascade".
referentialAction
    : CASCADE       # CascadeAction
    | NO ACTION     # NoActionAction
    | RESTRICT      # RestrictAction
    | SET NULL      # SetNullAction
    | SET DEFAULT   # SetDefaultAction
    ;

// Access's two append-query forms:
//   single-record    INSERT INTO target [(field, …)] VALUES (value, …)
//   multiple-record  INSERT INTO target [(field, …)] SELECT [source.]field, … FROM tableexpression
// plus `INSERT INTO t DEFAULT VALUES` — EF Core emits that for an all-store-generated/all-default row, and
// it inserts one row taking every column's default / AutoNumber.
//
// The IN externaldatabase clause both forms allow is deliberately absent: appending into another file is
// part of the linked/external-database subsystem, which LibRed neither reads nor writes.
//
// The multiple-record source is a queryExpression rather than a bare selectStatement, so a UNION can feed an
// append — the shape EF emits from a Concat — which is a superset of what Access documents.
insertStatement
    : INSERT INTO table=identifier
      ( (LPAREN columns+=identifier (COMMA columns+=identifier)* RPAREN)?
        ( VALUES rowValues (COMMA rowValues)*
        | source=queryExpression )
      | DEFAULT VALUES )
    ;

// One parenthesised row of a table value constructor. Access documents only a single row after VALUES, but
// the standard's constructor takes a comma-separated list of them and EF Core batches inserts that way, so
// LibRed accepts the list — a superset of Access, like the UNION source above. Every row must supply the same
// number of values as there are target columns, which the executor checks per row. SQL Server caps an
// INSERT ... VALUES at 1,000 rows; nothing here needs that limit, so none is imposed.
rowValues : LPAREN rowValue (COMMA rowValue)* RPAREN ;

// A row value is DEFAULT, NULL, or any expression. NULL needs no alternative of its own — it is already a
// literal. DEFAULT takes the column's declared default (or NULL when it has none), and the standard permits
// it only inside an INSERT, which falls out of `rowValue` appearing nowhere else in the grammar.
rowValue : DEFAULT | expression ;

// Set operations over SELECTs (left-associative). UNION dedupes; UNION ALL keeps
// duplicates; INTERSECT/EXCEPT dedupe. (Access has no INTERSECT/EXCEPT — LibRed owns the dialect.)
queryExpression : queryTerm (setOperator queryTerm)* ;
// A set-operation operand is a SELECT or a parenthesised query expression (so `A UNION ALL (B UNION C)`
// groups the right side as one term — EF emits this from Concat/Union nesting).
queryTerm
    : selectStatement                 # SelectTerm
    | LPAREN queryExpression RPAREN    # ParenTerm
    // A table value constructor standing in for a query — the standard's other use for it, beside the
    // INSERT clause. EF Core emits it for an inline collection, e.g.
    //   SELECT MAX(`v`.`Value`) FROM (SELECT CLNG(30) AS `Value` UNION ALL VALUES (`p`.`Int`)) AS `v`
    // where the row values may reference outer columns, so this is evaluated per outer row. Column names come
    // from the leading query of the set operation, per SQL, which is why no column alias list is needed here.
    | VALUES rowValues (COMMA rowValues)*  # ValuesTerm
    ;
setOperator : UNION ALL? | INTERSECT | EXCEPT ;

// The FROM clause is optional: ACE accepts a bare `SELECT 2` (verified) — a FROM-less SELECT yields one row.
//
// INTO makes it a MAKE-TABLE query: the rows go into a new table rather than back to the caller.
//   SELECT field1[, field2[, …]] INTO newtable [IN externaldatabase] FROM source
// The IN externaldatabase clause is deliberately absent, as it is on INSERT — creating a table in another
// file is part of the linked-database subsystem LibRed does not have.
selectStatement
    : SELECT predicate=selectPredicate? topClause? selectList (INTO into=identifier)? fromClause? whereClause? groupByClause? havingClause? orderByClause? offsetFetchClause?
    ;

// The optional row predicate. ALL is the default (return every row); DISTINCT dedupes on the output
// columns; DISTINCTROW dedupes on the underlying rows of the tables that contribute output columns
// (Access-specific — a no-op unless output is drawn from a strict subset of the joined tables).
selectPredicate : ALL | DISTINCT | DISTINCTROW ;

groupByClause : GROUP BY expression (COMMA expression)* ;
havingClause : HAVING expression ;

// Access allows only a literal after TOP, but LibRed also accepts a parameter (or a +/- expression of
// literals/parameters) — EFCore.Jet normally inlines the value, and we can evaluate it directly instead.
// Restricted to additive operands (no bare '*') so it can't swallow a following SELECT star ('TOP n *').
// A trailing PERCENT returns that percentage of rows (ceil) instead of a fixed count.
topClause : TOP topOperand ((PLUS | MINUS) topOperand)* percent=PERCENT? ;
topOperand : INTEGER_LITERAL | PARAM | LPAREN expression RPAREN ;

// ANSI SQL:2008 paging, which EF Core's base QuerySqlGenerator.GenerateLimitOffset emits whenever the
// provider does not rewrite Skip/Take into something dialect-specific. Three shapes:
//   Skip(n)          OFFSET n ROWS
//   Skip(n).Take(m)  OFFSET n ROWS FETCH NEXT m ROWS ONLY
//   Take(m)          FETCH FIRST m ROWS ONLY
// FIRST and NEXT are interchangeable in the standard, as are ROW and ROWS, so both spellings are accepted
// either side. Operands reuse topOperand, so a parameter is allowed where Access would demand a literal —
// EF passes the page size as @p, which is exactly what Jet's TOP cannot take.
offsetFetchClause
    : OFFSET offset=topOperand rowKeyword (FETCH (NEXT | FIRST) limit=topOperand rowKeyword ONLY)?
    | FETCH (FIRST | NEXT) limit=topOperand rowKeyword ONLY
    ;
rowKeyword : ROW | ROWS ;

selectList
    : STAR
    | selectItem (COMMA selectItem)*
    ;

selectItem
    : qualifier=identifier DOT STAR        # QualifiedStarSelectItem
    | expression (AS? alias=identifier)?   # ExpressionSelectItem
    ;

fromClause : FROM tableSource (COMMA tableSource)* ;

// A table source and its explicit JOINs; comma between sources is an implicit cross join.
tableSource : tablePrimary joinClause* ;

tablePrimary
    : table=identifier (AS? alias=identifier)?                  # NamedTablePrimary
    | LPAREN queryExpression RPAREN (AS? alias=identifier)?     # SubqueryPrimary
    | LPAREN tableSource RPAREN                                 # ParenJoinPrimary
    ;

// An ON condition is required for the conditional join types and forbidden for CROSS JOIN, which pairs every
// row with every row and so has nothing to condition on. Access has no CROSS JOIN keyword — a cartesian
// product is written there as comma-separated sources in the FROM clause, which `fromClause` still accepts
// and which builds the identical tree. EF Core's base generator emits the explicit form, so LibRed takes
// both spellings for the same thing.
//
// CROSS/OUTER APPLY are lateral joins and a LibRed extension - ACE has neither. The right side is evaluated
// once per left row with that row's columns in scope, so it may correlate to the left, which an ordinary
// join's right side may not. CROSS APPLY drops a left row whose right side came back empty; OUTER APPLY
// keeps it, null-padded, the way a LEFT JOIN does. Neither takes an ON: the correlation inside the right
// side is the condition. The position here follows T-SQL, which lists
// `left_table_source { CROSS | OUTER } APPLY right_table_source` as a <joined_table> alternative next to the
// conditional joins and CROSS JOIN.
joinClause
    : joinType JOIN tablePrimary ON expression   # ConditionalJoin
    | CROSS JOIN tablePrimary                    # CrossJoin
    | CROSS APPLY tablePrimary                   # CrossApply
    | OUTER APPLY tablePrimary                   # OuterApply
    ;

// FULL [OUTER] JOIN is a LibRed extension: ACE has no full outer join at all, and no way to express one
// (its query designer offers only the three above). FULL is therefore a keyword here that is not reserved in
// Access, so a column actually named "Full" has to be bracketed or backticked - the same tax LEFT, RIGHT,
// ORDER and every other keyword already charge.
joinType
    : INNER?            # InnerJoin
    | LEFT OUTER?       # LeftJoin
    | RIGHT OUTER?      # RightJoin
    | FULL OUTER?       # FullJoin
    ;

whereClause : WHERE expression ;

orderByClause : ORDER BY orderByItem (COMMA orderByItem)* ;
orderByItem : expression (dir=(ASC | DESC))? ;

expression
    : NOT expression                                                        # NotExpr
    | BNOT expression                                                       # BitNotExpr
    | MINUS expression                                                      # NegateExpr
    | left=expression CARET right=expression                                 # PowExpr
    | left=expression op=(STAR | SLASH | MOD | BACKSLASH) right=expression   # MulDivExpr
    | left=expression op=(PLUS | MINUS | AMP) right=expression               # AddConcatExpr
    | left=expression op=(EQ | NEQ | LT | LTE | GT | GTE) right=expression   # ComparisonExpr
    | val=expression not=NOT? BETWEEN lo=expression AND hi=expression        # BetweenExpr
    | left=expression not=NOT? LIKE right=expression                        # LikeExpr
    | val=expression not=NOT? IN LPAREN sub=selectStatement RPAREN                            # InSubqueryExpr
    | val=expression not=NOT? IN LPAREN items+=expression (COMMA items+=expression)* RPAREN  # InExpr
    | operand=expression IS not=NOT? NULL                                   # IsNullExpr
    | left=expression op=(BAND | BOR | BXOR) right=expression               # BitwiseExpr
    | left=expression AND right=expression                                  # AndExpr
    | left=expression OR right=expression                                   # OrExpr
    | primary                                                               # PrimaryExpr
    ;

primary
    : literal                          # LiteralPrimary
    | caseExpression                   # CasePrimary
    | functionCall                     # FunctionCallPrimary
    | columnRef                        # ColumnPrimary
    | PARAM                            # ParamPrimary
    | SYSVAR                           # SystemVariablePrimary
    | EXISTS LPAREN selectStatement RPAREN # ExistsPrimary
    | LPAREN selectStatement RPAREN    # ScalarSubqueryPrimary
    | LPAREN expression RPAREN         # ParenPrimary
    ;

// Standard SQL CASE, in both ANSI forms. Access/ACE has neither — it only has the IIF() function, which is
// why the Jet SQL generator rewrites a CASE into nested IIFs and LibRed's extended mode does not.
//   searched: CASE WHEN cond THEN result [WHEN …] [ELSE result] END
//   simple:   CASE operand WHEN value THEN result [WHEN …] [ELSE result] END
// EF Core emits both; its CaseExpression carries an optional Operand that selects between the two. The
// simple form compares operand = value, so it is sugar for the searched one and is folded into it here
// rather than kept as a separate node.
caseExpression
    : CASE operand=expression? caseWhen+ (ELSE elseResult=expression)? END
    ;
caseWhen : WHEN condition=expression THEN result=expression ;

// An optional DISTINCT before the argument applies to aggregates (COUNT/SUM/AVG/…): the aggregate operates
// on the distinct set of the argument's VALUES (COUNT(DISTINCT col)), not on distinct rows — see DISTINCTROW.
functionCall : name=functionName LPAREN (star=STAR | (distinct=DISTINCT? expression (COMMA expression)*))? RPAREN ;
// A function name is an identifier, or the LEFT/RIGHT/ASC keywords used as the Left()/Right()/Asc() functions —
// unambiguous with LEFT/RIGHT JOIN and ORDER BY ... ASC because a function call is always followed by '(' and
// never appears in the FROM/ORDER BY clause.
// Keywords that are also function names have to be readmitted here or the lexer's keyword token wins and the
// call stops parsing: Left/Right/Asc, and FIRST — which `offsetFetchClause` needs as a keyword for
// `FETCH FIRST`, but which is also the Access aggregate First(). (LAST is not listed because nothing else
// claims it as a keyword.)
functionName : identifier | LEFT | RIGHT | ASC | FIRST ;

columnRef : (qualifier=identifier DOT)? name=identifier ;

identifier : IDENTIFIER | BRACKET_ID | BACKTICK_ID ;

literal
    : INTEGER_LITERAL   # IntLiteral
    | NUMBER_LITERAL    # NumberLiteral
    | HEX_LITERAL       # HexLiteral
    | STRING_LITERAL    # StringLiteral
    | DATE_LITERAL      # DateLiteral
    | GUID_LITERAL      # GuidLiteral
    | TRUE              # TrueLiteral
    | FALSE             # FalseLiteral
    | NULL              # NullLiteral
    ;

// Transaction control (engine-native BEGIN/COMMIT/ROLLBACK, with the optional TRANSACTION/WORK keyword).
// These manage the transaction rather than run inside one, so the engine routes them straight to the
// transaction controller (exempt from the implicit per-statement wrap).
transactionStatement
    : BEGIN (TRANSACTION | WORK)?     # BeginTransactionStatement
    | COMMIT (TRANSACTION | WORK)?    # CommitTransactionStatement
    | ROLLBACK (TRANSACTION | WORK)?  # RollbackTransactionStatement
    ;

// A standalone expression read from catalog metadata (DEFAULT/CHECK/validation text). Requiring
// EOF prevents a valid prefix from silently weakening the stored expression's intended meaning.
// Kept after the existing parser rules so adding it does not renumber their generated rule ids.
standaloneExpression : expression EOF ;

// ---- Lexer ----

SELECT : [Ss][Ee][Ll][Ee][Cc][Tt] ;
FROM   : [Ff][Rr][Oo][Mm] ;
WHERE  : [Ww][Hh][Ee][Rr][Ee] ;
TOP    : [Tt][Oo][Pp] ;
AS     : [Aa][Ss] ;
AND    : [Aa][Nn][Dd] ;
OR     : [Oo][Rr] ;
NOT    : [Nn][Oo][Tt] ;
BAND   : [Bb][Aa][Nn][Dd] ;
BOR    : [Bb][Oo][Rr] ;
BXOR   : [Bb][Xx][Oo][Rr] ;
BNOT   : [Bb][Nn][Oo][Tt] ;
LIKE   : [Ll][Ii][Kk][Ee] ;
MOD    : [Mm][Oo][Dd] ;
INNER  : [Ii][Nn][Nn][Ee][Rr] ;
LEFT   : [Ll][Ee][Ff][Tt] ;
RIGHT  : [Rr][Ii][Gg][Hh][Tt] ;
FULL   : [Ff][Uu][Ll][Ll] ;
OUTER  : [Oo][Uu][Tt][Ee][Rr] ;
JOIN   : [Jj][Oo][Ii][Nn] ;
IN     : [Ii][Nn] ;
ON     : [Oo][Nn] ;
ORDER  : [Oo][Rr][Dd][Ee][Rr] ;
GROUP  : [Gg][Rr][Oo][Uu][Pp] ;
IS     : [Ii][Ss] ;
BY     : [Bb][Yy] ;
HAVING : [Hh][Aa][Vv][Ii][Nn][Gg] ;
EXISTS : [Ee][Xx][Ii][Ss][Tt][Ss] ;
IF     : [Ii][Ff] ;
THEN   : [Tt][Hh][Ee][Nn] ;
DISTINCTROW : [Dd][Ii][Ss][Tt][Ii][Nn][Cc][Tt][Rr][Oo][Ww] ;
DISTINCT : [Dd][Ii][Ss][Tt][Ii][Nn][Cc][Tt] ;
PERCENT  : [Pp][Ee][Rr][Cc][Ee][Nn][Tt] ;
CROSS    : [Cc][Rr][Oo][Ss][Ss] ;
APPLY    : [Aa][Pp][Pp][Ll][Yy] ;
CASE     : [Cc][Aa][Ss][Ee] ;
WHEN     : [Ww][Hh][Ee][Nn] ;
ELSE     : [Ee][Ll][Ss][Ee] ;
END      : [Ee][Nn][Dd] ;
OFFSET   : [Oo][Ff][Ff][Ss][Ee][Tt] ;
FETCH    : [Ff][Ee][Tt][Cc][Hh] ;
NEXT     : [Nn][Ee][Xx][Tt] ;
FIRST    : [Ff][Ii][Rr][Ss][Tt] ;
ROWS     : [Rr][Oo][Ww][Ss] ;
ROW      : [Rr][Oo][Ww] ;
ONLY     : [Oo][Nn][Ll][Yy] ;
BETWEEN  : [Bb][Ee][Tt][Ww][Ee][Ee][Nn] ;
UNION     : [Uu][Nn][Ii][Oo][Nn] ;
ALL       : [Aa][Ll][Ll] ;
INTERSECT : [Ii][Nn][Tt][Ee][Rr][Ss][Ee][Cc][Tt] ;
EXCEPT    : [Ee][Xx][Cc][Ee][Pp][Tt] ;
CREATE    : [Cc][Rr][Ee][Aa][Tt][Ee] ;
TABLE     : [Tt][Aa][Bb][Ll][Ee] ;
BEGIN       : [Bb][Ee][Gg][Ii][Nn] ;
COMMIT      : [Cc][Oo][Mm][Mm][Ii][Tt] ;
ROLLBACK    : [Rr][Oo][Ll][Ll][Bb][Aa][Cc][Kk] ;
TRANSACTION : [Tt][Rr][Aa][Nn][Ss][Aa][Cc][Tt][Ii][Oo][Nn] ;
WORK        : [Ww][Oo][Rr][Kk] ;
ALTER     : [Aa][Ll][Tt][Ee][Rr] ;
RENAME    : [Rr][Ee][Nn][Aa][Mm][Ee] ;
TO        : [Tt][Oo] ;
ADD       : [Aa][Dd][Dd] ;
DROP      : [Dd][Rr][Oo][Pp] ;
COLUMN    : [Cc][Oo][Ll][Uu][Mm][Nn] ;
INSERT    : [Ii][Nn][Ss][Ee][Rr][Tt] ;
INTO      : [Ii][Nn][Tt][Oo] ;
VALUES    : [Vv][Aa][Ll][Uu][Ee][Ss] ;
PRIMARY   : [Pp][Rr][Ii][Mm][Aa][Rr][Yy] ;
KEY       : [Kk][Ee][Yy] ;
CONSTRAINT : [Cc][Oo][Nn][Ss][Tt][Rr][Aa][Ii][Nn][Tt] ;
FOREIGN    : [Ff][Oo][Rr][Ee][Ii][Gg][Nn] ;
REFERENCES : [Rr][Ee][Ff][Ee][Rr][Ee][Nn][Cc][Ee][Ss] ;
DELETE     : [Dd][Ee][Ll][Ee][Tt][Ee] ;
UPDATE     : [Uu][Pp][Dd][Aa][Tt][Ee] ;
CASCADE    : [Cc][Aa][Ss][Cc][Aa][Dd][Ee] ;
RESTRICT   : [Rr][Ee][Ss][Tt][Rr][Ii][Cc][Tt] ;
ACTION     : [Aa][Cc][Tt][Ii][Oo][Nn] ;
SET        : [Ss][Ee][Tt] ;
DEFAULT    : [Dd][Ee][Ff][Aa][Uu][Ll][Tt] ;
NO         : [Nn][Oo] ;
UNIQUE     : [Uu][Nn][Ii][Qq][Uu][Ee] ;
INDEX      : [Ii][Nn][Dd][Ee][Xx] ;
TEMPORARY  : [Tt][Ee][Mm][Pp][Oo][Rr][Aa][Rr][Yy] ;
WITH       : [Ww][Ii][Tt][Hh] ;
COMPRESSION: [Cc][Oo][Mm][Pp][Rr][Ee][Ss][Ss][Ii][Oo][Nn] ;
COMP       : [Cc][Oo][Mm][Pp] ;
DISALLOW   : [Dd][Ii][Ss][Aa][Ll][Ll][Oo][Ww] ;
IGNORE     : [Ii][Gg][Nn][Oo][Rr][Ee] ;
CHECK      : [Cc][Hh][Ee][Cc][Kk] ;
VIEW       : [Vv][Ii][Ee][Ww] ;
PROCEDURE  : [Pp][Rr][Oo][Cc][Ee][Dd][Uu][Rr][Ee] ;
PARAMETERS : [Pp][Aa][Rr][Aa][Mm][Ee][Tt][Ee][Rr][Ss] ;
EXECUTE    : [Ee][Xx][Ee][Cc][Uu][Tt][Ee] ;
EXEC       : [Ee][Xx][Ee][Cc] ;
ASC    : [Aa][Ss][Cc] ;
DESC   : [Dd][Ee][Ss][Cc] ;
TRUE   : [Tt][Rr][Uu][Ee] ;
FALSE  : [Ff][Aa][Ll][Ss][Ee] ;
NULL   : [Nn][Uu][Ll][Ll] ;

STAR     : '*' ;
SLASH    : '/' ;
BACKSLASH: '\\' ;
CARET    : '^' ;
PLUS     : '+' ;
MINUS    : '-' ;
AMP      : '&' ;
EQ    : '=' ;
NEQ   : '<>' | '!=' ;
LTE   : '<=' ;
GTE   : '>=' ;
LT    : '<' ;
GT    : '>' ;
LPAREN : '(' ;
RPAREN : ')' ;
COMMA  : ',' ;
DOT    : '.' ;
SEMI   : ';' ;
// A connection-scoped system variable: @@ROWCOUNT (rows affected by the last statement) and
// @@IDENTITY (the last AutoNumber generated on this connection). Must precede PARAM so the '@@'
// prefix is matched as one token rather than PARAM failing on the second '@'.
SYSVAR : '@@' [A-Za-z_][A-Za-z_0-9]* ;
PARAM  : '?' | '@' [A-Za-z_][A-Za-z_0-9]* ;

// A raw binary literal, e.g. 0x151C2F… (an OLE / Long Binary value). Must precede INTEGER_LITERAL so the
// leading 0 isn't lexed as an integer; ANTLR's longest-match picks this, and ordering settles ties.
HEX_LITERAL     : '0' [Xx] [0-9A-Fa-f]+ ;
INTEGER_LITERAL : [0-9]+ ;
// A floating-point literal, with optional scientific-notation exponent (e.g. 6.023E23, 1.5e-3, 2E10).
// The bare-integer-with-exponent form (2E10) is a NUMBER_LITERAL, not an INTEGER_LITERAL — longest match.
NUMBER_LITERAL  : [0-9]+ '.' [0-9]* EXPONENT? | '.' [0-9]+ EXPONENT? | [0-9]+ EXPONENT ;
fragment EXPONENT : [Ee] [+-]? [0-9]+ ;
// A doubled quote inside a string is an escaped quote ('Bon app''' → Bon app'); the AST un-doubles it.
STRING_LITERAL  : '"' ( ~["] | '""' )* '"' | '\'' ( ~['] | '\'\'' )* '\'' ;
DATE_LITERAL    : '#' ~[#]* '#' ;
// Access GUID literal: {8-4-4-4-12 hex}. Braces appear nowhere else in the grammar.
GUID_LITERAL    : '{' HEXDIGIT+ '-' HEXDIGIT+ '-' HEXDIGIT+ '-' HEXDIGIT+ '-' HEXDIGIT+ '}' ;
fragment HEXDIGIT : [0-9A-Fa-f] ;
BRACKET_ID      : '[' ~[\]]+ ']' ;
BACKTICK_ID     : '`' ~[`]+ '`' ;
// A trailing '$' is allowed so VBA "$" string-function variants (Left$, UCase$, Chr$, …) lex as a single
// identifier. Longest-match makes "Left$" an IDENTIFIER (5 chars) rather than the LEFT keyword (4); the
// evaluator strips the '$' and dispatches to the base function.
IDENTIFIER      : [A-Za-z_][A-Za-z_0-9]* '$'? ;

WS      : [ \t\r\n]+ -> skip ;
// SQL comments — EF Core query tags prepend a `-- tag` line comment to the statement; also block comments.
LINE_COMMENT  : '--' ~[\r\n]* -> skip ;
BLOCK_COMMENT : '/*' .*? '*/' -> skip ;
