CREATE TABLE `AllDataTypes` (
	`AllDataTypesID` int IDENTITY PRIMARY KEY,
	`bigintColumn` int NOT NULL,
	`bitColumn` bit NOT NULL,
	`decimalColumn` decimal NOT NULL,
	`intColumn` int NOT NULL,
	`moneyColumn` money NOT NULL,
	`numericColumn` numeric NOT NULL,
	`smallintColumn` smallint NOT NULL,
	`smallmoneyColumn` money NOT NULL,
	`tinyintColumn` byte NOT NULL,
	`floatColumn` float NOT NULL,
	`realColumn` real NULL,
	`dateColumn` datetime NOT NULL,
	`datetimeColumn` datetime NULL,
	`datetime2Column` datetime NULL,
	`datetime24Column` datetime NULL,
	`datetimeoffsetColumn` datetime NULL,
	`datetimeoffset5Column` datetime  NULL,
	`smalldatetimeColumn` datetime NULL,
	`timeColumn` datetime NULL,
	`time4Column` datetime NULL,
	`charColumn` char NULL,
	`textColumn` text NULL,
	`varcharColumn` varchar NULL,
	`ncharColumn` char NULL,
	`ntextColumn` text NULL,
	`nvarcharColumn` varchar NULL,
	`binaryColumn` binary NULL,
	`imageColumn` image NULL,
	`varbinaryColumn` varbinary NULL,
	`timestampColumn` varbinary(8) NULL,
	`uniqueidentifierColumn` guid NULL,
	`hierarchyidColumn` text NULL,
	`xmlColumn` text NULL,
	`geographyColumn` text NULL,
	`geometryColumn` text NULL
)

GO


CREATE TABLE `PropertyConfiguration` (
	`PropertyConfigurationID` byte IDENTITY(1, 1) PRIMARY KEY, 
	`WithDateDefaultExpression` datetime NOT NULL DEFAULT Now(),
	`WithDateFixedDefault` datetime NOT NULL DEFAULT #10/20/2015#,
	`WithDateNullDefault` datetime NULL DEFAULT NULL,
	`WithGuidDefaultExpression` guid NOT NULL,
	`WithVarcharNullDefaultValue` varchar(255) NULL DEFAULT NULL,
	`WithDefaultValue` int NOT NULL DEFAULT -1,
	`WithNullDefaultValue` smallint NULL DEFAULT NULL,
	`WithMoneyDefaultValue` money NOT NULL DEFAULT 0.00,
	`A` int NOT NULL,
	`B` int NOT NULL,
	`SumOfAAndB` int, 
	`RowversionColumn` varbinary(8) NOT NULL
)

GO

CREATE INDEX Test_PropertyConfiguration_Index
	ON `PropertyConfiguration` (A, B)

GO

CREATE TABLE `Test Spaces Keywords Table` (
	`Test Spaces Keywords TableID` int PRIMARY KEY,
	`abstract` int NOT NULL,
	`class` int NULL,
	`volatile` int NOT NULL,
	`Spaces In Column` int NULL,
	`TabsInColumn` int NOT NULL,
	`@AtSymbolAtStartOfColumn` int NULL,
	`@Multiple@At@Symbols@In@Column` int NOT NULL,
	`Commas,In,Column` int NULL,
	`$Dollar$Sign$Column` int NOT NULL,
	`#Exclamation#Mark#Column` int NULL,
	`""Double""Quotes""Column` int NULL,
	`\Backslashes\In\Column` int NULL
)

GO

CREATE TABLE `SelfReferencing` (
	`SelfReferencingID` int PRIMARY KEY,
	`Name` varchar(20) NOT NULL,
	`Description` varchar(100) NOT NULL,
	`SelfReferenceFK` int NULL,
	CONSTRAINT `FK_SelfReferencing` FOREIGN KEY 
	(
		`SelfReferenceFK`
	) REFERENCES `SelfReferencing` (
		`SelfReferencingID`
	)
)

GO

CREATE TABLE `OneToManyPrincipal` (
	`OneToManyPrincipalID1` int,
	`OneToManyPrincipalID2` int,
	`Other` varchar(20) NOT NULL,
	CONSTRAINT `PK_OneToManyPrincipal` PRIMARY KEY  CLUSTERED 
	(
		`OneToManyPrincipalID1`, `OneToManyPrincipalID2`
	)
)

GO

CREATE TABLE `OneToManyDependent` (
	`OneToManyDependentID1` int,
	`OneToManyDependentID2` int,
	`SomeDependentEndColumn` varchar (20) NOT NULL,
	`OneToManyDependentFK2` int NULL, 
	`OneToManyDependentFK1` int NULL,
	CONSTRAINT `PK_OneToManyDependent` PRIMARY KEY  CLUSTERED 
	(
		`OneToManyDependentID1`, `OneToManyDependentID2`
	),
	CONSTRAINT `FK_OneToManyDependent` FOREIGN KEY 
	(
		`OneToManyDependentFK1`, `OneToManyDependentFK2`
	) REFERENCES `OneToManyPrincipal` (
		`OneToManyPrincipalID1`, `OneToManyPrincipalID2`
	)
)

GO

CREATE TABLE `OneToOnePrincipal` (
	`OneToOnePrincipalID1` int,
	`OneToOnePrincipalID2` int,
	`SomeOneToOnePrincipalColumn` varchar (20) NOT NULL,
	CONSTRAINT `PK_OneToOnePrincipal` PRIMARY KEY  CLUSTERED 
	(
		`OneToOnePrincipalID1`, `OneToOnePrincipalID2`
	)
)

GO

CREATE TABLE `OneToOneDependent` (
	`OneToOneDependentID1` int,
	`OneToOneDependentID2` int,
	`SomeDependentEndColumn` varchar (20) NOT NULL,
	CONSTRAINT `PK_OneToOneDependent` PRIMARY KEY  CLUSTERED 
	(
		`OneToOneDependentID1`, `OneToOneDependentID2`
	),
	CONSTRAINT `FK_OneToOneDependent` FOREIGN KEY 
	(
		`OneToOneDependentID1`, `OneToOneDependentID2`
	) REFERENCES `OneToOnePrincipal` (
		`OneToOnePrincipalID1`, `OneToOnePrincipalID2`
	)
)

GO

CREATE TABLE `OneToOneSeparateFKPrincipal` (
	`OneToOneSeparateFKPrincipalID1` int,
	`OneToOneSeparateFKPrincipalID2` int,
	`SomeOneToOneSeparateFKPrincipalColumn` varchar (20) NOT NULL,
	CONSTRAINT `PK_OneToOneSeparateFKPrincipal` PRIMARY KEY  CLUSTERED 
	(
		`OneToOneSeparateFKPrincipalID1`, `OneToOneSeparateFKPrincipalID2`
	)
)

GO

CREATE TABLE `OneToOneSeparateFKDependent` (
	`OneToOneSeparateFKDependentID1` int,
	`OneToOneSeparateFKDependentID2` int,
	`SomeDependentEndColumn` varchar (20) NOT NULL,
	`OneToOneSeparateFKDependentFK1` int NULL,
	`OneToOneSeparateFKDependentFK2` int NULL,
	CONSTRAINT `PK_OneToOneSeparateFKDependent` PRIMARY KEY  CLUSTERED 
	(
		`OneToOneSeparateFKDependentID1`, `OneToOneSeparateFKDependentID2`
	),
	CONSTRAINT `FK_OneToOneSeparateFKDependent` FOREIGN KEY 
	(
		`OneToOneSeparateFKDependentFK1`, `OneToOneSeparateFKDependentFK2`
	) REFERENCES `OneToOneSeparateFKPrincipal` (
		`OneToOneSeparateFKPrincipalID1`, `OneToOneSeparateFKPrincipalID2`
	),
	CONSTRAINT `UK_OneToOneSeparateFKDependent` UNIQUE
	(
		`OneToOneSeparateFKDependentFK1`, `OneToOneSeparateFKDependentFK2`
	)
)

GO

CREATE TABLE `OneToOneFKToUniqueKeyPrincipal` (
	`OneToOneFKToUniqueKeyPrincipalID1` int,
	`OneToOneFKToUniqueKeyPrincipalID2` int,
	`SomePrincipalColumn` varchar (20) NOT NULL,
	`OneToOneFKToUniqueKeyPrincipalUniqueKey1` int NOT NULL,
	`OneToOneFKToUniqueKeyPrincipalUniqueKey2` int NOT NULL,
	CONSTRAINT `PK_OneToOneFKToUniqueKeyPrincipal` PRIMARY KEY CLUSTERED 
	(
		`OneToOneFKToUniqueKeyPrincipalID1`, `OneToOneFKToUniqueKeyPrincipalID2`
	),
	CONSTRAINT `UK_OneToOneFKToUniqueKeyPrincipal` UNIQUE
	(
		`OneToOneFKToUniqueKeyPrincipalUniqueKey1`, `OneToOneFKToUniqueKeyPrincipalUniqueKey2`
	)
)

GO

CREATE TABLE `OneToOneFKToUniqueKeyDependent` (
	`OneToOneFKToUniqueKeyDependentID1` int,
	`OneToOneFKToUniqueKeyDependentID2` int,
	`SomeColumn` varchar (20) NOT NULL,
	`OneToOneFKToUniqueKeyDependentFK1` int NULL,
	`OneToOneFKToUniqueKeyDependentFK2` int NULL,
	CONSTRAINT `PK_OneToOneFKToUniqueKeyDependent` PRIMARY KEY  CLUSTERED 
	(
		`OneToOneFKToUniqueKeyDependentID1`, `OneToOneFKToUniqueKeyDependentID2`
	),
	CONSTRAINT `FK_OneToOneFKToUniqueKeyDependent` FOREIGN KEY 
	(
		`OneToOneFKToUniqueKeyDependentFK1`, `OneToOneFKToUniqueKeyDependentFK2`
	) REFERENCES `OneToOneFKToUniqueKeyPrincipal` (
		`OneToOneFKToUniqueKeyPrincipalUniqueKey1`, `OneToOneFKToUniqueKeyPrincipalUniqueKey2`
	),
	CONSTRAINT `UK_OneToOneFKToUniqueKeyDependent` UNIQUE
	(
		`OneToOneFKToUniqueKeyDependentFK1`, `OneToOneFKToUniqueKeyDependentFK2`
	)
)

GO

CREATE TABLE `ReferredToByTableWithUnmappablePrimaryKeyColumn` (
	`ReferredToByTableWithUnmappablePrimaryKeyColumnID` int PRIMARY KEY,
	`AColumn` varchar(20) NOT NULL,
	`ValueGeneratedOnAddColumn` int IDENTITY(1, 1) NOT NULL
)

GO

CREATE TABLE `TableWithUnmappablePrimaryKeyColumn` (
	`TableWithUnmappablePrimaryKeyColumnID` int PRIMARY KEY,
	`AnotherColumn` varchar(20) NOT NULL,
	`TableWithUnmappablePrimaryKeyColumnFK` int NULL,
	CONSTRAINT `FK_TableWithUnmappablePrimaryKeyColumn` FOREIGN KEY 
	(
		`TableWithUnmappablePrimaryKeyColumnFK`
	) REFERENCES `ReferredToByTableWithUnmappablePrimaryKeyColumn` (
		`ReferredToByTableWithUnmappablePrimaryKeyColumnID`
	),
	CONSTRAINT `UK_TableWithUnmappablePrimaryKeyColumn` UNIQUE
	(
		`AnotherColumn` 
	)
)

GO

CREATE TABLE `FilteredOut` (
	`FilteredOutID` int PRIMARY KEY,
	`Unused1` varchar(20) NOT NULL,
	`Unused2` int NOT NULL
)

GO
