-- Create Users table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Users' AND xtype='U')
BEGIN
    CREATE TABLE [Users] (
        [Id] int IDENTITY(1,1) NOT NULL,
        [Username] nvarchar(50) NOT NULL,
        [Email] nvarchar(100) NOT NULL,
        [PasswordHash] nvarchar(100) NOT NULL,
        [FirstName] nvarchar(50) NULL,
        [LastName] nvarchar(50) NULL,
        [Role] int NOT NULL DEFAULT 0,
        [IsActive] bit NOT NULL DEFAULT 1,
        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        [LastLoginAt] datetime2 NULL,
        [CreatedBy] nvarchar(50) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(50) NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );

    -- Create indexes
    CREATE UNIQUE INDEX [IX_Users_Username] ON [Users] ([Username]);
    CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
    CREATE INDEX [IX_Users_IsActive] ON [Users] ([IsActive]);

    -- Insert default admin user
    INSERT INTO [Users] ([Username], [Email], [PasswordHash], [FirstName], [LastName], [Role], [IsActive])
    VALUES ('admin', 'admin@nickscan.com', 'jGl25bVBBBW96Qi9Te4V37Fnqchz/Eu4qB9vKrRIqRg=', 'System', 'Administrator', 2, 1);

    PRINT 'Users table created successfully with default admin user!';
END
ELSE
BEGIN
    PRINT 'Users table already exists.';
END
