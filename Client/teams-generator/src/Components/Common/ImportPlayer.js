import Files from 'react-files'
import { PlayersContext } from '../../Store/PlayersContext';
import { useContext } from 'react';
import { message } from 'antd';


function ImportPlayer(props) {
    const [messageApi, contextHolder] = message.useMessage();

    const playersContext = useContext(PlayersContext)


    function readFileHandler(files) {
        const key = 'updatable';

        const fileReader = new FileReader();

        fileReader.onload = e => {
            playersContext.setPlayers(JSON.parse(e.target.result))
            messageApi.open({
                key,
                type: 'success',
                content: 'Loaded!',
                duration: 2,
            })
        };
        messageApi.open({
            key,
            type: 'loading',
            content: 'Loading...',
        })
        fileReader.readAsText(files[0], "UTF-8");
    }

    return (<Files
        className='files-dropzone'
        onChange={readFileHandler}
        accepts={['.json']}
        maxFileSize={10000000}
        minFileSize={0}
        clickable>
        <>
        {contextHolder}
        {props.children}
        </>
    </Files>)
}

export default ImportPlayer