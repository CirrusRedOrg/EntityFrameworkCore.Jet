// ANTLR4 grammar for the Jet/ACE (Microsoft Access) SQL dialect.
//
// Scope: SELECT with projection/aliases, multi-table FROM with INNER/LEFT/RIGHT JOIN and
// derived-table subqueries, WHERE, ORDER BY, TOP. The parse tree is lowered into
// LibRed.Sql.Ast by AstBuilder, so the rest of the engine never sees these generated types.
//
// Dialect notes (vs ANSI): '&' string concat; MOD / '\' operators; '*'/'?' LIKE wildcards;
// TOP n (no OFFSET); #1/1/2020# date literals; [bracketed] and `backtick` identifiers;
// booleans -1/0.

grammar AccessSql;

// A single statement, optionally terminated by ';' (EF Core emits a trailing semicolon).
statement : parametersClause? (createTableStatement | createIndexStatement | createViewStatement | createProcedureStatement | alterTableStatement | insertStatement | queryExpression) SEMI? EOF ;

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
    | ALTER COLUMN? field=identifier dataType    # AlterColumnAction
    | DROP COLUMN field=identifier               # DropColumnAction
    | DROP CONSTRAINT cname=identifier           # DropConstraintAction
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

indexColumn : col=identifier dir=(ASC | DESC)? ;

withOption
    : PRIMARY        # WithPrimary
    | DISALLOW NULL  # WithDisallowNull
    | IGNORE NULL    # WithIgnoreNull
    ;

columnDefinition : name=identifier dataType columnConstraint* ;

// A second word handles two-word ANSI aliases like CHARACTER VARYING / BIT VARYING.
dataType : typeName=identifier extra=identifier? (LPAREN size=INTEGER_LITERAL (COMMA scale=INTEGER_LITERAL)? RPAREN)? ;

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

insertStatement
    : INSERT INTO table=identifier
      (LPAREN columns+=identifier (COMMA columns+=identifier)* RPAREN)?
      VALUES LPAREN expression (COMMA expression)* RPAREN
    ;

// Set operations over SELECTs (left-associative). UNION dedupes; UNION ALL keeps
// duplicates; INTERSECT/EXCEPT dedupe. (Access has no INTERSECT/EXCEPT — LibRed owns the dialect.)
queryExpression : selectStatement (setOperator selectStatement)* ;
setOperator : UNION ALL? | INTERSECT | EXCEPT ;

selectStatement
    : SELECT distinct=DISTINCT? topClause? selectList fromClause whereClause? groupByClause? havingClause? orderByClause?
    ;

groupByClause : GROUP BY expression (COMMA expression)* ;
havingClause : HAVING expression ;

// Access allows only a literal after TOP, but LibRed also accepts a parameter (or a +/- expression of
// literals/parameters) — EFCore.Jet normally inlines the value, and we can evaluate it directly instead.
// Restricted to additive operands (no bare '*') so it can't swallow a following SELECT star ('TOP n *').
topClause : TOP topOperand ((PLUS | MINUS) topOperand)* ;
topOperand : INTEGER_LITERAL | PARAM | LPAREN expression RPAREN ;

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

joinClause : joinType JOIN tablePrimary ON expression ;

joinType
    : INNER?            # InnerJoin
    | LEFT OUTER?       # LeftJoin
    | RIGHT OUTER?      # RightJoin
    ;

whereClause : WHERE expression ;

orderByClause : ORDER BY orderByItem (COMMA orderByItem)* ;
orderByItem : expression (dir=(ASC | DESC))? ;

expression
    : NOT expression                                                        # NotExpr
    | MINUS expression                                                      # NegateExpr
    | left=expression CARET right=expression                                 # PowExpr
    | left=expression op=(STAR | SLASH | MOD | BACKSLASH) right=expression   # MulDivExpr
    | left=expression op=(PLUS | MINUS | AMP) right=expression               # AddConcatExpr
    | left=expression op=(EQ | NEQ | LT | LTE | GT | GTE) right=expression   # ComparisonExpr
    | val=expression not=NOT? BETWEEN lo=expression AND hi=expression        # BetweenExpr
    | left=expression LIKE right=expression                                 # LikeExpr
    | operand=expression IS not=NOT? NULL                                   # IsNullExpr
    | left=expression AND right=expression                                  # AndExpr
    | left=expression OR right=expression                                   # OrExpr
    | primary                                                               # PrimaryExpr
    ;

primary
    : literal                          # LiteralPrimary
    | functionCall                     # FunctionCallPrimary
    | columnRef                        # ColumnPrimary
    | PARAM                            # ParamPrimary
    | EXISTS LPAREN selectStatement RPAREN # ExistsPrimary
    | LPAREN selectStatement RPAREN    # ScalarSubqueryPrimary
    | LPAREN expression RPAREN         # ParenPrimary
    ;

functionCall : name=identifier LPAREN (star=STAR | (expression (COMMA expression)*))? RPAREN ;

columnRef : (qualifier=identifier DOT)? name=identifier ;

identifier : IDENTIFIER | BRACKET_ID | BACKTICK_ID ;

literal
    : INTEGER_LITERAL   # IntLiteral
    | NUMBER_LITERAL    # NumberLiteral
    | HEX_LITERAL       # HexLiteral
    | STRING_LITERAL    # StringLiteral
    | DATE_LITERAL      # DateLiteral
    | TRUE              # TrueLiteral
    | FALSE             # FalseLiteral
    | NULL              # NullLiteral
    ;

// ---- Lexer ----

SELECT : [Ss][Ee][Ll][Ee][Cc][Tt] ;
FROM   : [Ff][Rr][Oo][Mm] ;
WHERE  : [Ww][Hh][Ee][Rr][Ee] ;
TOP    : [Tt][Oo][Pp] ;
AS     : [Aa][Ss] ;
AND    : [Aa][Nn][Dd] ;
OR     : [Oo][Rr] ;
NOT    : [Nn][Oo][Tt] ;
LIKE   : [Ll][Ii][Kk][Ee] ;
MOD    : [Mm][Oo][Dd] ;
INNER  : [Ii][Nn][Nn][Ee][Rr] ;
LEFT   : [Ll][Ee][Ff][Tt] ;
RIGHT  : [Rr][Ii][Gg][Hh][Tt] ;
OUTER  : [Oo][Uu][Tt][Ee][Rr] ;
JOIN   : [Jj][Oo][Ii][Nn] ;
ON     : [Oo][Nn] ;
ORDER  : [Oo][Rr][Dd][Ee][Rr] ;
GROUP  : [Gg][Rr][Oo][Uu][Pp] ;
IS     : [Ii][Ss] ;
BY     : [Bb][Yy] ;
HAVING : [Hh][Aa][Vv][Ii][Nn][Gg] ;
EXISTS : [Ee][Xx][Ii][Ss][Tt][Ss] ;
DISTINCT : [Dd][Ii][Ss][Tt][Ii][Nn][Cc][Tt] ;
BETWEEN  : [Bb][Ee][Tt][Ww][Ee][Ee][Nn] ;
UNION     : [Uu][Nn][Ii][Oo][Nn] ;
ALL       : [Aa][Ll][Ll] ;
INTERSECT : [Ii][Nn][Tt][Ee][Rr][Ss][Ee][Cc][Tt] ;
EXCEPT    : [Ee][Xx][Cc][Ee][Pp][Tt] ;
CREATE    : [Cc][Rr][Ee][Aa][Tt][Ee] ;
TABLE     : [Tt][Aa][Bb][Ll][Ee] ;
ALTER     : [Aa][Ll][Tt][Ee][Rr] ;
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
PARAM  : '?' | '@' [A-Za-z_][A-Za-z_0-9]* ;

// A raw binary literal, e.g. 0x151C2F… (an OLE / Long Binary value). Must precede INTEGER_LITERAL so the
// leading 0 isn't lexed as an integer; ANTLR's longest-match picks this, and ordering settles ties.
HEX_LITERAL     : '0' [Xx] [0-9A-Fa-f]+ ;
INTEGER_LITERAL : [0-9]+ ;
NUMBER_LITERAL  : [0-9]+ '.' [0-9]* | '.' [0-9]+ ;
// A doubled quote inside a string is an escaped quote ('Bon app''' → Bon app'); the AST un-doubles it.
STRING_LITERAL  : '"' ( ~["] | '""' )* '"' | '\'' ( ~['] | '\'\'' )* '\'' ;
DATE_LITERAL    : '#' ~[#]* '#' ;
BRACKET_ID      : '[' ~[\]]+ ']' ;
BACKTICK_ID     : '`' ~[`]+ '`' ;
IDENTIFIER      : [A-Za-z_][A-Za-z_0-9]* ;

WS      : [ \t\r\n]+ -> skip ;
