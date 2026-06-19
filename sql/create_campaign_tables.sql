IF OBJECT_ID(N'[dbo].[Campaigns]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Campaigns] (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Campaigns] PRIMARY KEY,
        [Name] NVARCHAR(160) NOT NULL,
        [Purpose] NVARCHAR(80) NOT NULL,
        [Channel] NVARCHAR(40) NOT NULL,
        [Status] NVARCHAR(40) NOT NULL CONSTRAINT [DF_Campaigns_Status] DEFAULT (N'Draft'),
        [Subject] NVARCHAR(200) NULL,
        [MessageTemplate] NVARCHAR(MAX) NOT NULL,
        [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_Campaigns_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [ScheduledAt] DATETIME2 NULL,
        [SentAt] DATETIME2 NULL,
        [CreatedBy] NVARCHAR(120) NOT NULL CONSTRAINT [DF_Campaigns_CreatedBy] DEFAULT (N'')
    );
END;

IF OBJECT_ID(N'[dbo].[CampaignRecipients]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CampaignRecipients] (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CampaignRecipients] PRIMARY KEY,
        [CampaignId] INT NOT NULL,
        [ContactId] INT NOT NULL,
        [SourceEventId] INT NULL,
        [RecipientAddress] NVARCHAR(320) NOT NULL,
        [Status] NVARCHAR(40) NOT NULL CONSTRAINT [DF_CampaignRecipients_Status] DEFAULT (N'Pending'),
        [ProviderMessageId] NVARCHAR(120) NULL,
        [ErrorMessage] NVARCHAR(MAX) NULL,
        [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_CampaignRecipients_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [SentAt] DATETIME2 NULL,
        CONSTRAINT [FK_CampaignRecipients_Campaigns_CampaignId] FOREIGN KEY ([CampaignId]) REFERENCES [dbo].[Campaigns] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_CampaignRecipients_Contacts_ContactId] FOREIGN KEY ([ContactId]) REFERENCES [dbo].[Contacts] ([Id]),
        CONSTRAINT [FK_CampaignRecipients_Events_SourceEventId] FOREIGN KEY ([SourceEventId]) REFERENCES [dbo].[Events] ([Id])
    );
END;

IF OBJECT_ID(N'[dbo].[CommunicationLogs]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CommunicationLogs] (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CommunicationLogs] PRIMARY KEY,
        [CampaignId] INT NULL,
        [ContactId] INT NULL,
        [Channel] NVARCHAR(40) NOT NULL,
        [Recipient] NVARCHAR(320) NOT NULL,
        [Status] NVARCHAR(40) NOT NULL,
        [ProviderResponse] NVARCHAR(MAX) NULL,
        [ErrorMessage] NVARCHAR(MAX) NULL,
        [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_CommunicationLogs_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [FK_CommunicationLogs_Campaigns_CampaignId] FOREIGN KEY ([CampaignId]) REFERENCES [dbo].[Campaigns] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_CommunicationLogs_Contacts_ContactId] FOREIGN KEY ([ContactId]) REFERENCES [dbo].[Contacts] ([Id]) ON DELETE SET NULL
    );
END;

IF OBJECT_ID(N'[dbo].[ContactConsents]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ContactConsents] (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ContactConsents] PRIMARY KEY,
        [ContactId] INT NOT NULL,
        [Purpose] NVARCHAR(80) NOT NULL,
        [Channel] NVARCHAR(40) NOT NULL,
        [Accepted] BIT NOT NULL,
        [AcceptedAt] DATETIME2 NULL,
        [RevokedAt] DATETIME2 NULL,
        [Source] NVARCHAR(120) NOT NULL CONSTRAINT [DF_ContactConsents_Source] DEFAULT (N''),
        [Notes] NVARCHAR(MAX) NULL,
        CONSTRAINT [FK_ContactConsents_Contacts_ContactId] FOREIGN KEY ([ContactId]) REFERENCES [dbo].[Contacts] ([Id]) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'[dbo].[ContactTags]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ContactTags] (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ContactTags] PRIMARY KEY,
        [Name] NVARCHAR(80) NOT NULL,
        [Color] NVARCHAR(20) NOT NULL CONSTRAINT [DF_ContactTags_Color] DEFAULT (N'')
    );
END;

IF OBJECT_ID(N'[dbo].[ContactTagAssignments]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ContactTagAssignments] (
        [ContactId] INT NOT NULL,
        [ContactTagId] INT NOT NULL,
        [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_ContactTagAssignments_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_ContactTagAssignments] PRIMARY KEY ([ContactId], [ContactTagId]),
        CONSTRAINT [FK_ContactTagAssignments_Contacts_ContactId] FOREIGN KEY ([ContactId]) REFERENCES [dbo].[Contacts] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ContactTagAssignments_ContactTags_ContactTagId] FOREIGN KEY ([ContactTagId]) REFERENCES [dbo].[ContactTags] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Campaigns_Purpose_Channel_Status' AND [object_id] = OBJECT_ID(N'[dbo].[Campaigns]'))
BEGIN
    CREATE INDEX [IX_Campaigns_Purpose_Channel_Status] ON [dbo].[Campaigns] ([Purpose], [Channel], [Status]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'UX_CampaignRecipients_CampaignId_ContactId' AND [object_id] = OBJECT_ID(N'[dbo].[CampaignRecipients]'))
BEGIN
    CREATE UNIQUE INDEX [UX_CampaignRecipients_CampaignId_ContactId] ON [dbo].[CampaignRecipients] ([CampaignId], [ContactId]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'UX_ContactConsents_ContactId_Purpose_Channel' AND [object_id] = OBJECT_ID(N'[dbo].[ContactConsents]'))
BEGIN
    CREATE UNIQUE INDEX [UX_ContactConsents_ContactId_Purpose_Channel] ON [dbo].[ContactConsents] ([ContactId], [Purpose], [Channel]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'UX_ContactTags_Name' AND [object_id] = OBJECT_ID(N'[dbo].[ContactTags]'))
BEGIN
    CREATE UNIQUE INDEX [UX_ContactTags_Name] ON [dbo].[ContactTags] ([Name]);
END;

IF OBJECT_ID(N'[dbo].[CampaignAttachments]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CampaignAttachments] (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CampaignAttachments] PRIMARY KEY,
        [CampaignId] INT NOT NULL,
        [FileName] NVARCHAR(260) NOT NULL,
        [ContentType] NVARCHAR(120) NOT NULL,
        [Content] VARBINARY(MAX) NOT NULL,
        [Size] INT NOT NULL,
        [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_CampaignAttachments_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [FK_CampaignAttachments_Campaigns_CampaignId] FOREIGN KEY ([CampaignId]) REFERENCES [dbo].[Campaigns] ([Id]) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'[dbo].[SavedSegments]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SavedSegments] (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SavedSegments] PRIMARY KEY,
        [Name] NVARCHAR(160) NOT NULL,
        [FiltersJson] NVARCHAR(MAX) NOT NULL,
        [SegmentBy] NVARCHAR(40) NOT NULL CONSTRAINT [DF_SavedSegments_SegmentBy] DEFAULT (N'none'),
        [CreatedBy] NVARCHAR(120) NOT NULL CONSTRAINT [DF_SavedSegments_CreatedBy] DEFAULT (N''),
        [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_SavedSegments_CreatedAt] DEFAULT (SYSUTCDATETIME())
    );
END;
