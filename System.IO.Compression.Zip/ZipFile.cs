namespace System.IO.Compression.Zip
{
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Unique class for compression/decompression file. Represents a Zip file.
    /// </summary>
    public class ZipFile : IDisposable
    {
        #region Constants and Fields

        /// <summary>
        /// Static CRC32 Table
        /// </summary>
        private static readonly uint[] CrcTable;

        /// <summary>
        /// Default filename encoder
        /// </summary>
        private static readonly Encoding DefaultEncoding = Encoding.GetEncoding(437);

        /// <summary>
        /// Central dir image
        /// </summary>
        private byte[] centralDirImage;

        /// <summary>
        /// Stream object of storage file
        /// </summary>
        private Stream zipFileStream;

        #endregion

        #region Constructors and Destructors

        /// <summary>
        /// Initializes static members of the <see cref="ZipFile"/> class. 
        /// Static constructor. 
        /// Just invoked once in order to create the CRC32 lookup table.
        /// </summary>
        static ZipFile()
        {
            // Generate CRC32 table
            CrcTable = new uint[256];
            for (uint i = 0; i < CrcTable.Length; i++)
            {
                var c = i;

                for (int j = 0; j < 8; j++)
                {
                    if ((c & 1) != 0)
                    {
                        c = 3988292384 ^ (c >> 1);
                    }
                    else
                    {
                        c >>= 1;
                    }
                }

                CrcTable[i] = c;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ZipFile"/> class. 
        /// Method to open an existing storage file
        /// </summary>
        /// <param name="filename">
        /// Full path of Zip file to open
        /// </param>
        /// <returns>
        /// A valid ZipFile object
        /// </returns>
        public ZipFile(string filename)
            : this(new FileStream(filename, FileMode.Open, FileAccess.Read))
        {
            this.FileName = filename;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ZipFile"/> class. 
        /// Method to open an existing storage from stream
        /// </summary>
        /// <param name="stream">
        /// Already opened stream with zip contents
        /// </param>
        /// <returns>
        /// A valid ZipFile object
        /// </returns>
        public ZipFile(Stream stream)
        {
            if (!stream.CanSeek)
            {
                throw new InvalidOperationException("Stream cannot seek");
            }

            this.zipFileStream = stream;

            if (!this.ReadFileInfo())
            {
                throw new Exception();
            }

            this.FileName = string.Empty;
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets or sets FileName.
        /// </summary>
        public string FileName { get; set; }

        #endregion

        #region Public Methods and Operators

        /// <summary>
        /// DOS Date and time are packed values with the following format:
        ///   MS-DOS date bits description:
        ///     0-4 Day of the month (1�31) 
        ///     5-8 Month (1 = January, 2 = February, and so on) 
        ///     9-15 Year offset from 1980 (add 1980 to get actual year) 
        ///   MS-DOS time bits description:
        ///     0-4 Second divided by 2 
        ///     5-10 Minute (0�59) 
        ///     11-15 Hour (0�23 on a 24-hour clock) 
        /// </summary>
        /// <param name="dateTime">
        /// The date Time.
        /// </param>
        /// <returns>
        /// The date time to dos time.
        /// </returns>
        public static uint DateTimeToDosTime(DateTime dateTime)
        {
            return (uint)((dateTime.Second / 2) | // seconds
                          (dateTime.Minute << 5) | // minutes
                          (dateTime.Hour << 11) | // hours
                          (dateTime.Day << 16) | // days
                          (dateTime.Month << 21) | // months
                          ((dateTime.Year - 1980) << 25)); // years
        }

        /// <summary>
        /// Updates central directory (if pertinent) and close the Zip storage
        /// </summary>
        /// <remarks>
        /// This is a required step, unless automatic dispose is used
        /// </remarks>
        public void Close()
        {
            if (this.zipFileStream != null)
            {
                this.zipFileStream.Flush();
                this.zipFileStream.Dispose();
                this.zipFileStream = null;
            }
        }

        /// <summary>
        /// Closes the Zip file stream
        /// </summary>
        public void Dispose()
        {
            this.Close();
        }

        /// <summary>
        /// Copy the contents of a stored file into a physical file
        /// </summary>
        /// <param name="zfe">
        /// Entry information of file to extract
        /// </param>
        /// <param name="filename">
        /// Name of file to store uncompressed data
        /// </param>
        /// <returns>
        /// True if success, false if not.
        /// </returns>
        /// <remarks>
        /// Unique compression methods are Store and Deflate
        /// </remarks>
        public bool ExtractFile(ZipFileEntry zfe, string filename)
        {
            // Make sure the parent directory exist
            string path = Path.GetDirectoryName(filename);

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            // Check it is directory. If so, do nothing
            if (Directory.Exists(filename))
            {
                return true;
            }

            Stream output = new FileStream(filename, FileMode.Create, FileAccess.Write);
            bool result = this.ExtractFile(zfe, output);
            if (result)
            {
                output.Close();
            }

            File.SetCreationTime(filename, zfe.ModifyTime);
            File.SetLastWriteTime(filename, zfe.ModifyTime);

            return result;
        }

        /// <summary>
        /// Copy the contents of a stored file into an opened stream
        /// </summary>
        /// <param name="zfe">
        /// Entry information of file to extract
        /// </param>
        /// <param name="stream">
        /// Stream to store the uncompressed data
        /// </param>
        /// <returns>
        /// True if success, false if not.
        /// </returns>
        /// <remarks>
        /// Unique compression methods are Store and Deflate
        /// </remarks>
        public bool ExtractFile(ZipFileEntry zfe, Stream stream)
        {
            if (!stream.CanWrite)
            {
                throw new InvalidOperationException("Stream cannot be written");
            }

            // check signature
            var signature = new byte[4];
            this.zipFileStream.Seek(zfe.HeaderOffset, SeekOrigin.Begin);
            this.zipFileStream.Read(signature, 0, 4);
            if (BitConverter.ToUInt32(signature, 0) != 0x04034b50)
            {
                return false;
            }

            // Select input stream for inflating or just reading
            Stream inStream;
            if (zfe.Method == Compression.Store)
            {
                inStream = this.zipFileStream;
            }
            else if (zfe.Method == Compression.Deflate)
            {
                inStream = new DeflateStream(this.zipFileStream, CompressionMode.Decompress, true);
            }
            else
            {
                return false;
            }

            // Buffered copy
            var buffer = new byte[16384];
            this.zipFileStream.Seek(zfe.FileOffset, SeekOrigin.Begin);
            uint bytesPending = zfe.FileSize;
            while (bytesPending > 0)
            {
                int bytesRead = inStream.Read(buffer, 0, (int)Math.Min(bytesPending, buffer.Length));
                stream.Write(buffer, 0, bytesRead);
                bytesPending -= (uint)bytesRead;
            }

            stream.Flush();

            if (zfe.Method == Compression.Deflate)
            {
                inStream.Dispose();
            }

            return true;
        }

        /// <summary>
        /// Read all the file records in the central directory 
        /// </summary>
        /// <returns>
        /// List of all entries in directory.
        /// </returns>
        public ZipFileEntry[] GetAllEntries()
        {
            if (this.centralDirImage == null)
            {
                throw new InvalidOperationException("Central directory currently does not exist");
            }

            var result = new List<ZipFileEntry>();

            for (int pointer = 0; pointer < this.centralDirImage.Length;)
            {
                uint signature = BitConverter.ToUInt32(this.centralDirImage, pointer);
                if (signature != 0x02014b50)
                {
                    break;
                }

                result.Add(this.GetEntry(ref pointer));
            }

            return result.ToArray();
        }

        /// <summary>
        /// Copy the contents of a stored file into an opened stream
        /// </summary>
        /// <param name="zfe">
        /// Entry information of file to extract
        /// </param>
        /// <returns>
        /// Stream to store the uncompressed data
        /// </returns>
        /// <remarks>
        /// Unique compression methods are Store and Deflate
        /// </remarks>
        public Stream ReadFile(ZipFileEntry zfe)
        {
            // check signature
            var signature = new byte[4];
            this.zipFileStream.Seek(zfe.HeaderOffset, SeekOrigin.Begin);
            this.zipFileStream.Read(signature, 0, 4);
            if (BitConverter.ToUInt32(signature, 0) != 0x04034b50)
            {
                throw new InvalidOperationException("Not a valid zip entry.");
            }

            // Select input stream for inflating or just reading
            Stream inStream;
            switch (zfe.Method)
            {
                case Compression.Store:
                    inStream = this.zipFileStream;
                    break;
                case Compression.Deflate:
                    inStream = new DeflateStream(this.zipFileStream, CompressionMode.Decompress, true);
                    break;
                default:
                    throw new InvalidOperationException("Not a valid zip entry.");
            }

            this.zipFileStream.Seek(zfe.FileOffset, SeekOrigin.Begin);

            return new ZipStream(inStream, zfe);
        }

        #endregion

        #region Methods

        /// <summary>
        /// DOS Date and time are packed values with the following format:
        ///   MS-DOS date bits description:
        ///     0-4 Day of the month (1�31) 
        ///     5-8 Month (1 = January, 2 = February, and so on) 
        ///     9-15 Year offset from 1980 (add 1980 to get actual year) 
        ///   MS-DOS time bits description:
        ///     0-4 Second divided by 2 
        ///     5-10 Minute (0�59) 
        ///     11-15 Hour (0�23 on a 24-hour clock) 
        /// </summary>
        /// <param name="dosDateTime">
        /// The dos Date Time.
        /// </param>
        private static DateTime DosTimeToDateTime(uint dosDateTime)
        {
            return new DateTime(
                (int)(dosDateTime >> 25) + 1980, 
                (int)(dosDateTime >> 21) & 15, 
                (int)(dosDateTime >> 16) & 31, 
                (int)(dosDateTime >> 11) & 31, 
                (int)(dosDateTime >> 5) & 63, 
                (int)(dosDateTime & 31) * 2);
        }

        /// <summary>
        /// The get entry.
        /// </summary>
        /// <param name="pointer">
        /// The pointer.
        /// </param>
        /// <returns>
        /// </returns>
        private ZipFileEntry GetEntry(ref int pointer)
        {
            bool encodeUtf8 = (BitConverter.ToUInt16(this.centralDirImage, pointer + 8) & 0x0800) != 0;
            ushort method = BitConverter.ToUInt16(this.centralDirImage, pointer + 10);
            uint modifyTime = BitConverter.ToUInt32(this.centralDirImage, pointer + 12);
            uint crc32 = BitConverter.ToUInt32(this.centralDirImage, pointer + 16);
            uint comprSize = BitConverter.ToUInt32(this.centralDirImage, pointer + 20);
            uint fileSize = BitConverter.ToUInt32(this.centralDirImage, pointer + 24);
            ushort filenameSize = BitConverter.ToUInt16(this.centralDirImage, pointer + 28);
            ushort extraSize = BitConverter.ToUInt16(this.centralDirImage, pointer + 30);
            ushort commentSize = BitConverter.ToUInt16(this.centralDirImage, pointer + 32);
            uint headerOffset = BitConverter.ToUInt32(this.centralDirImage, pointer + 42);
            int commentsStart = 46 + filenameSize + extraSize;
            int headerSize = commentsStart + commentSize;
            Encoding encoder = encodeUtf8 ? Encoding.UTF8 : DefaultEncoding;
            string comment = null;
            if (commentSize > 0)
            {
                comment = encoder.GetString(this.centralDirImage, pointer + commentsStart, commentSize);
            }

            var zfe = new ZipFileEntry
                {
                    Method = (Compression)method, 
                    FilenameInZip = encoder.GetString(this.centralDirImage, pointer + 46, filenameSize), 
                    FileOffset = this.GetFileOffset(headerOffset), 
                    FileSize = fileSize, 
                    CompressedSize = comprSize, 
                    HeaderOffset = headerOffset, 
                    HeaderSize = (uint)headerSize, 
                    Crc32 = crc32, 
                    ModifyTime = DosTimeToDateTime(modifyTime), 
                    Comment = comment ?? string.Empty, 
                    ZipFileName = this.FileName
                };
            pointer += headerSize;
            return zfe;
        }

        /// <summary>
        /// The get file offset.
        /// </summary>
        /// <param name="headerOffset">
        /// The header offset.
        /// </param>
        /// <returns>
        /// The get file offset.
        /// </returns>
        private uint GetFileOffset(uint headerOffset)
        {
            var buffer = new byte[2];

            this.zipFileStream.Seek(headerOffset + 26, SeekOrigin.Begin);
            this.zipFileStream.Read(buffer, 0, 2);
            ushort filenameSize = BitConverter.ToUInt16(buffer, 0);
            this.zipFileStream.Read(buffer, 0, 2);
            ushort extraSize = BitConverter.ToUInt16(buffer, 0);

            return (uint)(30 + filenameSize + extraSize + headerOffset);
        }

        /// <summary>
        /// Reads the end-of-central-directory record
        /// </summary>
        /// <returns>
        /// True if the read was successful, false if there ws an error.
        /// </returns>
        private bool ReadFileInfo()
        {
            if (this.zipFileStream.Length < 22)
            {
                return false;
            }

            try
            {
                this.zipFileStream.Seek(-17, SeekOrigin.End);
                var br = new BinaryReader(this.zipFileStream);
                do
                {
                    this.zipFileStream.Seek(-5, SeekOrigin.Current);
                    uint sig = br.ReadUInt32();
                    if (sig == 0x06054b50)
                    {
                        this.zipFileStream.Seek(6, SeekOrigin.Current);

                        ushort entries = br.ReadUInt16();
                        int centralSize = br.ReadInt32();
                        uint centralDirOffset = br.ReadUInt32();
                        ushort commentSize = br.ReadUInt16();

                        // check if comment field is the very last data in file
                        if (this.zipFileStream.Position + commentSize != this.zipFileStream.Length)
                        {
                            return false;
                        }

                        // Copy entire central directory to a memory buffer
                        this.centralDirImage = new byte[centralSize];
                        this.zipFileStream.Seek(centralDirOffset, SeekOrigin.Begin);
                        this.zipFileStream.Read(this.centralDirImage, 0, centralSize);

                        // Leave the pointer at the begining of central dir, to append new files
                        this.zipFileStream.Seek(centralDirOffset, SeekOrigin.Begin);
                        return true;
                    }
                }
                while (this.zipFileStream.Position > 0);
            }
            catch (Exception ex)
            {
                Diagnostics.Debug.WriteLine(ex.Message);
            }

            return false;
        }

        #endregion
    }
}