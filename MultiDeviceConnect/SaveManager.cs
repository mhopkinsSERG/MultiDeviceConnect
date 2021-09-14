using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MultiDeviceConnect
{
    partial class SaveManager
    {
        #region Fields
        StreamWriter writer;
        SemaphoreSlim semaphoreLocker = new SemaphoreSlim(1, 1);        //Creates an object to limit the number of threads that can acess a resource.
        #endregion

        #region Constructor
        public SaveManager()
        {

        }
        #endregion

        #region Methods
        public bool InitSaveFileAsync(string filename, string title)
        {
            try
            {
                //Obtain the file directory:
                string path = Directory.GetCurrentDirectory();
                path = Directory.GetParent(path).Parent.Parent.FullName;
                path = path + "\\Device_Data\\";
                Directory.CreateDirectory(path);
                //Create the file:
                File.WriteAllText(path + filename, title + "\n");   //Use File.Write for a single write operation. StreamWriter is better for continuous writing.
                //Initialise the StreamWriter for future use:
                writer = new StreamWriter(path + filename, true);
                writer.AutoFlush = true;                            //StreamWriter is buffered by default, hence, it will not output until a Flush() or Close() call. Set it to automatically flush.
                return true;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
            return false;
        }

        public async Task<bool> SaveAsync(string message)
        {
            await semaphoreLocker.WaitAsync();       //Enters the semaphore.
            //Write the message to file (protected by the semaphore):
            try
            {
                await writer?.WriteLineAsync(message);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
                return false;
            }
            finally
            {
                semaphoreLocker.Release();       //Restores the semaphore count to its maximum value.
            }
            return true;
        }
        #endregion
    }
}
