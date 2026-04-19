// Image Viewer JavaScript Helpers

window.downloadImage = async function (imageUrl, fileName) {
    try {
        const response = await fetch(imageUrl);
        const blob = await response.blob();
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName || 'image.jpg';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        window.URL.revokeObjectURL(url);
    } catch (error) {
        console.error('Error downloading image:', error);
    }
};

// Pan and zoom functionality
window.initImagePan = function (imageId) {
    const image = document.getElementById(imageId);
    if (!image) return;

    let isDragging = false;
    let startX, startY, scrollLeft, scrollTop;
    const container = image.parentElement;

    image.addEventListener('mousedown', (e) => {
        isDragging = true;
        image.style.cursor = 'grabbing';
        startX = e.pageX - container.offsetLeft;
        startY = e.pageY - container.offsetTop;
        scrollLeft = container.scrollLeft;
        scrollTop = container.scrollTop;
    });

    image.addEventListener('mouseleave', () => {
        isDragging = false;
        image.style.cursor = 'grab';
    });

    image.addEventListener('mouseup', () => {
        isDragging = false;
        image.style.cursor = 'grab';
    });

    image.addEventListener('mousemove', (e) => {
        if (!isDragging) return;
        e.preventDefault();
        const x = e.pageX - container.offsetLeft;
        const y = e.pageY - container.offsetTop;
        const walkX = (x - startX) * 2;
        const walkY = (y - startY) * 2;
        container.scrollLeft = scrollLeft - walkX;
        container.scrollTop = scrollTop - walkY;
    });
};

