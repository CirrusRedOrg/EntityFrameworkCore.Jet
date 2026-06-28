// ANTLR4 grammar for the Jet/ACE (Microsoft Access) SQL dialect.
//
// Current scope: SELECT <list> FROM <table> [WHERE <predicate>]. Grows by adding rules;
// the parse tree is lowered into LibRed.Sql.Ast by AstBuildingVisitor, so the rest of the
// engine never depends on these generated types.
//
// Dialect notes (vs ANSI): '&' string concat; '*'/'?' LIKE wildcards; TOP n (no OFFSET);
// IIF()/Format()/Nz(); #1/1/2020# date literals; [bracketed identifiers]; booleans -1/0.

grammar AccessSql;

statement : selectStatement EOF ;

selectStatement
    : SELECT topClause? selectList FROM tableSource whereClause?
    ;

topClause : TOP INTEGER_LITERAL ;

selectList
    : STAR
    | selectItem (COMMA selectItem)*
    ;

selectItem : expression (AS? alias=identifier)? ;

tableSource : table=identifier (AS? alias=identifier)? ;

whereClause : WHERE expression ;

expression
    : NOT expression                                                  # NotExpr
    | left=expression op=(STAR | SLASH) right=expression              # MulDivExpr
    | left=expression op=(PLUS | MINUS | AMP) right=expression        # AddConcatExpr
    | left=expression op=(EQ | NEQ | LT | LTE | GT | GTE) right=expression  # ComparisonExpr
    | left=expression AND right=expression                            # AndExpr
    | left=expression OR right=expression                             # OrExpr
    | primary                                                         # PrimaryExpr
    ;

primary
    : literal                    # LiteralPrimary
    | columnRef                  # ColumnPrimary
    | PARAM                      # ParamPrimary
    | LPAREN expression RPAREN   # ParenPrimary
    ;

columnRef : (qualifier=identifier DOT)? name=identifier ;

identifier : IDENTIFIER | BRACKET_ID ;

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
TRUE   : [Tt][Rr][Uu][Ee] ;
FALSE  : [Ff][Aa][Ll][Ss][Ee] ;
NULL   : [Nn][Uu][Ll][Ll] ;

STAR  : '*' ;
SLASH : '/' ;
PLUS  : '+' ;
MINUS : '-' ;
AMP   : '&' ;
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
IDENTIFIER      : [A-Za-z_][A-Za-z_0-9]* ;

WS      : [ \t\r\n]+ -> skip ;
