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

statement : queryExpression EOF ;

// Set operations over SELECTs (left-associative). UNION dedupes; UNION ALL keeps
// duplicates; INTERSECT/EXCEPT dedupe. (Access has no INTERSECT/EXCEPT — LibRed owns the dialect.)
queryExpression : selectStatement (setOperator selectStatement)* ;
setOperator : UNION ALL? | INTERSECT | EXCEPT ;

selectStatement
    : SELECT topClause? selectList fromClause whereClause? groupByClause? havingClause? orderByClause?
    ;

groupByClause : GROUP BY expression (COMMA expression)* ;
havingClause : HAVING expression ;

topClause : TOP INTEGER_LITERAL ;

selectList
    : STAR
    | selectItem (COMMA selectItem)*
    ;

selectItem : expression (AS? alias=identifier)? ;

fromClause : FROM tableSource (COMMA tableSource)* ;

// A table source and its explicit JOINs; comma between sources is an implicit cross join.
tableSource : tablePrimary joinClause* ;

tablePrimary
    : table=identifier (AS? alias=identifier)?                  # NamedTablePrimary
    | LPAREN selectStatement RPAREN (AS? alias=identifier)?     # SubqueryPrimary
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
    | left=expression op=(STAR | SLASH | MOD | BACKSLASH) right=expression   # MulDivExpr
    | left=expression op=(PLUS | MINUS | AMP) right=expression               # AddConcatExpr
    | left=expression op=(EQ | NEQ | LT | LTE | GT | GTE) right=expression   # ComparisonExpr
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
    | STRING_LITERAL    # StringLiteral
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
UNION     : [Uu][Nn][Ii][Oo][Nn] ;
ALL       : [Aa][Ll][Ll] ;
INTERSECT : [Ii][Nn][Tt][Ee][Rr][Ss][Ee][Cc][Tt] ;
EXCEPT    : [Ee][Xx][Cc][Ee][Pp][Tt] ;
ASC    : [Aa][Ss][Cc] ;
DESC   : [Dd][Ee][Ss][Cc] ;
TRUE   : [Tt][Rr][Uu][Ee] ;
FALSE  : [Ff][Aa][Ll][Ss][Ee] ;
NULL   : [Nn][Uu][Ll][Ll] ;

STAR     : '*' ;
SLASH    : '/' ;
BACKSLASH: '\\' ;
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
PARAM  : '?' | '@' [A-Za-z_][A-Za-z_0-9]* ;

INTEGER_LITERAL : [0-9]+ ;
NUMBER_LITERAL  : [0-9]+ '.' [0-9]* | '.' [0-9]+ ;
STRING_LITERAL  : '"' (~["])* '"' | '\'' (~['])* '\'' ;
BRACKET_ID      : '[' ~[\]]+ ']' ;
BACKTICK_ID     : '`' ~[`]+ '`' ;
IDENTIFIER      : [A-Za-z_][A-Za-z_0-9]* ;

WS      : [ \t\r\n]+ -> skip ;
