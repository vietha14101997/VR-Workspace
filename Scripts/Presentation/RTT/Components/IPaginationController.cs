/// <summary>
/// Interface for pagination controller used by RTTFilePagination.
/// Allows both RTTFileManagerController and RTTMediaLibraryController to use the same pagination component.
/// </summary>

namespace VRWorkspace.UI.RTT.Components
{
    public interface IPaginationController
    {
        /// <summary>
        /// Change page by delta (-1 for previous, +1 for next).
        /// </summary>
        void ChangePage(int delta);

        /// <summary>
        /// Go to specific page number.
        /// </summary>
        void GoToPage(int pageNumber);

        /// <summary>
        /// Scroll to first page.
        /// </summary>
        void ScrollToStart();

        /// <summary>
        /// Scroll to last page.
        /// </summary>
        void ScrollToEnd();
    }

}
