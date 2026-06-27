// ANTLR4 grammar for the Jet/ACE (Microsoft Access) SQL dialect.
//
// This file is intentionally a starting skeleton. It is kept as a plain file (not
// wired into the build) so the project compiles without the ANTLR tool installed.
// To enable code generation, see the commented PackageReference block in
// LibRed.Sql.csproj, then flesh out the rules below.
//
// Dialect notes that make Access SQL differ from ANSI:
//   * String concatenation uses '&' (and '+'); strings are delimited by " or '.
//   * Wildcards in LIKE are '*' and '?' (ANSI '%' / '_' under ANSI-92 mode).
//   * TOP n  instead of LIMIT; no OFFSET.
//   * IIF(), SWITCH(), Format(), Nz(), and VBA-style date literals (#1/1/2020#).
//   * Bracketed identifiers [Order Details]; parameters are positional or named.
//   * Jet-specific joins: nested (INNER JOIN ... ) chains, and the Access-only
//     "Iif"/domain aggregate functions (DLookup, DCount, ...).

grammar AccessSql;

// ---- Parser rules -------------------------------------------------------------

statement
    : selectStatement
    | insertStatement
    | updateStatement
    | deleteStatement
    ;

selectStatement : SELECT topClause? selectList FROM tableSource whereClause? ;

insertStatement : INSERT INTO IDENTIFIER /* ... */ ;
updateStatement : UPDATE IDENTIFIER SET /* ... */ ;
deleteStatement : DELETE FROM IDENTIFIER whereClause? ;

topClause  : TOP INTEGER_LITERAL ;
selectList : STAR | expression (COMMA expression)* ;
tableSource : IDENTIFIER ;            // TODO: joins, subqueries, aliases
whereClause : WHERE expression ;

expression : IDENTIFIER | STRING_LITERAL | INTEGER_LITERAL ; // TODO

// ---- Lexer rules --------------------------------------------------------------

SELECT : [Ss][Ee][Ll][Ee][Cc][Tt] ;
FROM   : [Ff][Rr][Oo][Mm] ;
WHERE  : [Ww][Hh][Ee][Rr][Ee] ;
INSERT : [Ii][Nn][Ss][Ee][Rr][Tt] ;
INTO   : [Ii][Nn][Tt][Oo] ;
UPDATE : [Uu][Pp][Dd][Aa][Tt][Ee] ;
DELETE : [Dd][Ee][Ll][Ee][Tt][Ee] ;
SET    : [Ss][Ee][Tt] ;
TOP    : [Tt][Oo][Pp] ;

STAR   : '*' ;
COMMA  : ',' ;

IDENTIFIER      : [A-Za-z_][A-Za-z_0-9]* | '[' ~[\]]+ ']' ;
INTEGER_LITERAL : [0-9]+ ;
STRING_LITERAL  : '"' (~["])* '"' | '\'' (~['])* '\'' ;

WS      : [ \t\r\n]+ -> skip ;
